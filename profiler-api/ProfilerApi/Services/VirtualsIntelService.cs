using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace ProfilerApi.Services;

/// <summary>
/// Fetches Virtuals Protocol ecosystem data from CoinGecko (free API, no key required).
/// Returns VIRTUAL token stats and top AI agent tokens.
/// </summary>
public class VirtualsIntelService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VirtualsIntelService> _logger;

    // Known Virtuals ecosystem tokens with CoinGecko IDs
    private static readonly Dictionary<string, string> EcosystemTokens = new()
    {
        ["VIRTUAL"] = "virtual-protocol",
        ["AIXBT"] = "aixbt",
        ["GAME"] = "game-by-virtuals",
        ["LUNA"] = "luna-by-virtuals-protocols",
        ["VADER"] = "vader-ai-by-virtuals",
        ["SEKOIA"] = "sekoia-by-virtuals",
        ["AIMONICA"] = "aimonica-brands",
        ["MISATO"] = "misato",
        ["CONVO"] = "convo-by-virtuals",
        ["BIO"] = "biopunk"
    };

    private const string CacheKey = "virtuals_ecosystem";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public VirtualsIntelService(HttpClient httpClient, IMemoryCache cache, ILogger<VirtualsIntelService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<VirtualsEcosystemReport> GetEcosystemReportAsync(string? query = null)
    {
        // Check cache
        if (_cache.TryGetValue(CacheKey, out VirtualsEcosystemReport? cached) && cached != null)
            return cached;

        var report = new VirtualsEcosystemReport();

        try
        {
            // Fetch all ecosystem token prices from CoinGecko
            var ids = string.Join(",", EcosystemTokens.Values);
            var url = $"https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&ids={ids}&order=market_cap_desc&sparkline=false&price_change_percentage=24h,7d";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WalletProfiler/2.9");
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                report.Error = $"CoinGecko API returned {response.StatusCode}";
                return report;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokens = JsonNode.Parse(json)?.AsArray();

            if (tokens == null)
            {
                report.Error = "Failed to parse CoinGecko response";
                return report;
            }

            // Map CoinGecko ID back to symbol
            var idToSymbol = EcosystemTokens.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var token in tokens)
            {
                var cgId = token?["id"]?.GetValue<string>() ?? "";
                var symbol = idToSymbol.GetValueOrDefault(cgId, token?["symbol"]?.GetValue<string>()?.ToUpperInvariant() ?? "UNKNOWN");

                var tokenData = new VirtualsTokenData
                {
                    Symbol = symbol,
                    Name = token?["name"]?.GetValue<string>() ?? "",
                    PriceUsd = token?["current_price"]?.GetValue<decimal?>() ?? 0,
                    MarketCap = token?["market_cap"]?.GetValue<decimal?>(),
                    Volume24h = token?["total_volume"]?.GetValue<decimal?>(),
                    PriceChange24hPct = token?["price_change_percentage_24h"]?.GetValue<decimal?>(),
                    PriceChange7dPct = token?["price_change_percentage_7d_in_currency"]?.GetValue<decimal?>(),
                    Rank = token?["market_cap_rank"]?.GetValue<int?>(),
                    ImageUrl = token?["image"]?.GetValue<string>()
                };

                if (symbol == "VIRTUAL")
                    report.VirtualToken = tokenData;
                else
                    report.AgentTokens.Add(tokenData);
            }

            // Sort agent tokens by market cap
            report.AgentTokens = report.AgentTokens
                .OrderByDescending(t => t.MarketCap ?? 0)
                .ToList();

            // Calculate ecosystem totals
            var allTokens = new List<VirtualsTokenData>();
            if (report.VirtualToken != null) allTokens.Add(report.VirtualToken);
            allTokens.AddRange(report.AgentTokens);

            report.EcosystemMarketCap = allTokens.Sum(t => t.MarketCap ?? 0);
            report.EcosystemVolume24h = allTokens.Sum(t => t.Volume24h ?? 0);
            report.TokenCount = allTokens.Count;

            // Ecosystem health heuristic
            var virtualChange = report.VirtualToken?.PriceChange24hPct ?? 0;
            var avgAgentChange = report.AgentTokens.Count > 0
                ? report.AgentTokens.Average(t => (double)(t.PriceChange24hPct ?? 0))
                : 0;

            report.EcosystemHealth = virtualChange switch
            {
                > 5 => "bullish",
                > 0 => "stable",
                > -5 => "cautious",
                _ => "bearish"
            };

            report.Summary = $"VIRTUAL at ${report.VirtualToken?.PriceUsd:F4} ({virtualChange:+0.0;-0.0}% 24h). " +
                $"Ecosystem: {report.TokenCount} tokens tracked, ${report.EcosystemMarketCap:N0} total market cap, " +
                $"${report.EcosystemVolume24h:N0} 24h volume. Sentiment: {report.EcosystemHealth}.";

            _cache.Set(CacheKey, report, CacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Virtuals ecosystem data");
            report.Error = ex.Message;
        }

        return report;
    }
}

public class VirtualsEcosystemReport
{
    public VirtualsTokenData? VirtualToken { get; set; }
    public List<VirtualsTokenData> AgentTokens { get; set; } = [];
    public decimal EcosystemMarketCap { get; set; }
    public decimal EcosystemVolume24h { get; set; }
    public int TokenCount { get; set; }
    public string EcosystemHealth { get; set; } = "unknown";
    public string? Summary { get; set; }
    public string? Error { get; set; }
    public DateTime QueriedAt { get; set; } = DateTime.UtcNow;
}

public class VirtualsTokenData
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceUsd { get; set; }
    public decimal? MarketCap { get; set; }
    public decimal? Volume24h { get; set; }
    public decimal? PriceChange24hPct { get; set; }
    public decimal? PriceChange7dPct { get; set; }
    public int? Rank { get; set; }
    public string? ImageUrl { get; set; }
}
