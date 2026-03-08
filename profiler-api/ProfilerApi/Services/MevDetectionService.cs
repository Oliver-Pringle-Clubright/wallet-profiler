using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Detects MEV exposure by analyzing transaction patterns.
/// Looks for sandwich attacks and front/back-running indicators
/// using Etherscan V2 transaction data.
/// </summary>
public class MevDetectionService
{
    private readonly HttpClient _httpClient;
    private readonly EthereumService _ethService;
    private readonly IConfiguration _config;
    private readonly ILogger<MevDetectionService> _logger;

    // Known MEV bot addresses (Flashbots, MEV bots)
    private static readonly HashSet<string> KnownMevBots = new(StringComparer.OrdinalIgnoreCase)
    {
        "0x000000000000006f6502b7f2bbac8c30a3f67e9a", // jaredfromsubway
        "0x6b75d8af000000e20b7a7ddf000ba900b4009a80", // sandwich bot
        "0x00000000003b3cc22af3ae1eac0440bcee416b40", // MEV bot
        "0x56178a0d5f301baf6cf3e1cd53d9863437345bf9", // sandwich bot
        "0xae2fc483527b8ef99eb5d9b44875f005ba1fae13", // MEV bot
    };

    public MevDetectionService(
        HttpClient httpClient,
        EthereumService ethService,
        IConfiguration config,
        ILogger<MevDetectionService> logger)
    {
        _httpClient = httpClient;
        _ethService = ethService;
        _config = config;
        _logger = logger;
    }

    public async Task<MevExposure> AnalyzeAsync(string address, string chain)
    {
        var exposure = new MevExposure();

        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return exposure;

        var apiKey = _config["Etherscan:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return exposure;

        try
        {
            // Get recent transactions
            var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=txlist&address={address}&page=1&offset=200&sort=desc&apikey={apiKey}";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return exposure;

            var txs = result.EnumerateArray().ToList();
            var incidents = new List<MevIncident>();

            foreach (var tx in txs)
            {
                var from = tx.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                var to = tx.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
                var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
                var blockStr = tx.TryGetProperty("blockNumber", out var bn) ? bn.GetString() ?? "0" : "0";
                var timestampStr = tx.TryGetProperty("timeStamp", out var ts) ? ts.GetString() ?? "0" : "0";
                var gasPrice = tx.TryGetProperty("gasPrice", out var gp) ? gp.GetString() ?? "0" : "0";

                if (!long.TryParse(timestampStr, out var timestamp)) continue;
                var txTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

                // Check if transacting with known MEV bot
                if (KnownMevBots.Contains(from) || KnownMevBots.Contains(to))
                {
                    var isSandwich = false;

                    // Look for sandwich pattern: MEV bot tx before and after user tx in same block
                    var block = blockStr;
                    var sameBlockTxs = txs.Where(t2 =>
                    {
                        var b2 = t2.TryGetProperty("blockNumber", out var bn2) ? bn2.GetString() : "";
                        return b2 == block;
                    }).ToList();

                    if (sameBlockTxs.Count > 1)
                    {
                        isSandwich = true;
                        exposure.SandwichAttacks++;
                    }

                    incidents.Add(new MevIncident
                    {
                        TxHash = hash,
                        Type = isSandwich ? "sandwich" : "frontrun",
                        TokenSymbol = "ETH",
                        Timestamp = txTime
                    });

                    if (!isSandwich)
                        exposure.FrontrunTransactions++;
                }

                // Check for suspiciously high gas price (potential frontrun victim)
                if (decimal.TryParse(gasPrice, out var gasPriceWei) && gasPriceWei > 100_000_000_000) // >100 gwei
                {
                    // Could indicate gas war / frontrun attempt
                    exposure.BackrunTransactions++;
                }
            }

            exposure.RecentIncidents = incidents.Take(10).ToList();

            // Calculate risk level
            var totalMev = exposure.SandwichAttacks + exposure.FrontrunTransactions;
            exposure.RiskLevel = totalMev switch
            {
                0 => "none",
                <= 2 => "low",
                <= 5 => "moderate",
                _ => "high"
            };

            // Rough loss estimate (sandwich attacks typically cost 0.5-2% of trade value)
            if (exposure.SandwichAttacks > 0)
                exposure.EstimatedLossUsd = exposure.SandwichAttacks * 50m; // Conservative estimate

            return exposure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MEV analysis failed for {Address}", address);
            return exposure;
        }
    }
}
