using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class AiAnalystService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiAnalystService> _logger;

    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-sonnet-4-20250514";

    public AiAnalystService(HttpClient httpClient, IConfiguration config, IMemoryCache cache, ILogger<AiAnalystService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_config["Anthropic:ApiKey"]);

    public async Task<AiAnalysisResponse> AnalyzeAsync(WalletProfile profile, string question)
    {
        var cacheKey = $"ai_analysis_{profile.Address}_{question.GetHashCode()}";
        if (_cache.TryGetValue(cacheKey, out AiAnalysisResponse? cached) && cached != null)
            return cached;

        var apiKey = _config["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Anthropic API key not configured");

        var profileSummary = BuildProfileSummary(profile);
        var systemPrompt = """
            You are an expert DeFi and blockchain analyst. You analyze wallet profiles and provide clear, actionable insights.
            When answering questions, be specific with numbers and percentages from the profile data.
            Structure your response with:
            1. A direct answer to the question (2-3 sentences)
            2. Key insights (3-5 bullet points)
            3. Recommendations (2-3 actionable items)
            Keep responses concise and focused. Do not use markdown formatting.
            """;

        var userPrompt = $"""
            Wallet Profile Data:
            {profileSummary}

            User Question: {question}
            """;

        var requestBody = new
        {
            model = _config["Anthropic:Model"] ?? DefaultModel,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        var request = new HttpRequestMessage(HttpMethod.Post, AnthropicApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error ({Status}): {Body}", response.StatusCode, responseBody);
            throw new Exception($"AI analysis failed ({response.StatusCode})");
        }

        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);
        var analysisText = apiResponse?.Content?.FirstOrDefault()?.Text ?? "Analysis unavailable";

        var result = ParseAnalysis(profile.Address, question, analysisText);
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    private static string BuildProfileSummary(WalletProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Address: {profile.Address}");
        if (profile.EnsName != null) sb.AppendLine($"ENS: {profile.EnsName}");
        sb.AppendLine($"ETH Balance: {profile.EthBalance:F4} ETH (${profile.EthValueUsd:F2})");
        sb.AppendLine($"Total Value: ${profile.TotalValueUsd:F2}");
        sb.AppendLine($"Transactions: {profile.TransactionCount}");
        sb.AppendLine($"Risk: {profile.Risk.Level} (score {profile.Risk.Score})");
        sb.AppendLine($"Tags: {string.Join(", ", profile.Tags)}");

        if (profile.PortfolioQuality != null)
            sb.AppendLine($"Portfolio Quality: {profile.PortfolioQuality.Grade} (score {profile.PortfolioQuality.QualityScore}, bluechip {profile.PortfolioQuality.BluechipPct}%, stable {profile.PortfolioQuality.StablecoinPct}%)");

        if (profile.TopTokens.Count > 0)
        {
            sb.AppendLine($"Top Tokens ({profile.TopTokens.Count(t => !t.IsSpam)} non-spam):");
            foreach (var t in profile.TopTokens.Where(t => !t.IsSpam && t.ValueUsd > 0).OrderByDescending(t => t.ValueUsd).Take(10))
                sb.AppendLine($"  {t.Symbol}: {t.Balance:F4} (${t.ValueUsd:F2})");
        }

        if (profile.DeFiPositions.Count > 0)
        {
            sb.AppendLine($"DeFi Positions ({profile.DeFiPositions.Count}):");
            foreach (var d in profile.DeFiPositions.Take(10))
                sb.AppendLine($"  {d.Protocol} [{d.Type}]: {d.Amount:F4} {d.Asset}");
        }

        if (profile.Activity != null)
            sb.AppendLine($"Activity: {profile.Activity.DaysActive} days active, {profile.Activity.UniqueInteractions} unique interactions");

        if (profile.SmartMoney != null)
            sb.AppendLine($"Smart Money: {profile.SmartMoney.Classification} (profit score {profile.SmartMoney.ProfitScore})");

        if (profile.Pnl != null)
            sb.AppendLine($"P&L: ${profile.Pnl.TotalPnlUsd:F2} ({profile.Pnl.TotalPnlPct:F1}%), {profile.Pnl.ProfitableTokens} winners / {profile.Pnl.LosingTokens} losers");

        if (profile.LiquidationRisk != null)
            sb.AppendLine($"Liquidation Risk: {profile.LiquidationRisk.OverallRisk} (Aave HF: {profile.LiquidationRisk.AaveHealthFactor:F2})");

        if (profile.LpPositions.Count > 0)
            sb.AppendLine($"LP Positions: {profile.LpPositions.Count} ({profile.LpPositions.Count(p => p.InRange)} in range)");

        return sb.ToString();
    }

    private static AiAnalysisResponse ParseAnalysis(string address, string question, string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var insights = new List<string>();
        var recommendations = new List<string>();
        var currentSection = "analysis";

        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("key insight") || lower.Contains("key finding"))
            {
                currentSection = "insights";
                continue;
            }
            if (lower.Contains("recommendation") || lower.Contains("action"))
            {
                currentSection = "recommendations";
                continue;
            }

            var cleaned = line.TrimStart('-', '*', ' ', '\t');
            if (string.IsNullOrEmpty(cleaned)) continue;

            // Remove numbered list prefixes
            if (cleaned.Length > 2 && char.IsDigit(cleaned[0]) && (cleaned[1] == '.' || cleaned[1] == ')'))
                cleaned = cleaned[2..].TrimStart();

            if (currentSection == "insights" && cleaned.Length > 5)
                insights.Add(cleaned);
            else if (currentSection == "recommendations" && cleaned.Length > 5)
                recommendations.Add(cleaned);
        }

        return new AiAnalysisResponse
        {
            Address = address,
            Question = question,
            Analysis = text,
            KeyInsights = insights.Take(5).ToList(),
            Recommendations = recommendations.Take(3).ToList()
        };
    }

    // Anthropic API response models
    private class AnthropicResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
