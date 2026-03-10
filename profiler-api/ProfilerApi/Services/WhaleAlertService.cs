using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Detects recent large token transfers on a chain by monitoring
/// known whale/exchange addresses via Etherscan V2.
/// </summary>
public class WhaleAlertService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WhaleAlertService> _logger;

    // Known high-activity addresses to monitor for large movements
    private static readonly Dictionary<string, string> WatchedAddresses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Exchanges
        ["0x28C6c06298d514Db089934071355E5743bf21d60"] = "Binance Hot Wallet",
        ["0x21a31Ee1afC51d94C2eFcCAa2092aD1028285549"] = "Binance Hot Wallet 2",
        ["0xDFd5293D8e347dFe59E90eFd55b2956a1343963d"] = "Binance Hot Wallet 3",
        ["0x503828976D22510aad0339F4fB8fB7B3F6C5cA4B"] = "Coinbase",
        ["0xA9D1e08C7793af67e9d92fe308d5697FB81d3E43"] = "Coinbase 10",
        ["0x2910543Af39abA0Cd09dBb2D50200b3E800A63D2"] = "Kraken",
        ["0x267be1C1D684F78cb4F6a176C4911b741E4Ffdc0"] = "Kraken 4",
        ["0x56Eddb7aa87536c09CCc2793473599fD21A8b17F"] = "Bitfinex",
        ["0x46340b20830761efd32832A74d7169B29FEB9758"] = "Crypto.com",
        // Bridges / Protocols
        ["0x40ec5B33f54e0E8A33A975908C5BA1c14e5BbbDf"] = "Polygon Bridge",
        ["0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"] = "USDC Contract",
        ["0xdAC17F958D2ee523a2206206994597C13D831ec7"] = "USDT Contract",
        // Whales / Notable
        ["0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"] = "vitalik.eth",
        ["0x00000000219ab540356cBB839Cbe05303d7705Fa"] = "ETH2 Deposit Contract",
    };

    public WhaleAlertService(HttpClient httpClient, IConfiguration config, ILogger<WhaleAlertService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<WhaleAlertResponse> GetRecentWhaleTransfersAsync(string chain = "ethereum", int hours = 24, decimal minValueUsd = 100_000m)
    {
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return new WhaleAlertResponse { Chain = chain, Error = $"Unsupported chain: {chain}" };

        var apiKey = _config["Etherscan:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return new WhaleAlertResponse { Chain = chain, Error = "Etherscan API key not configured" };

        var cutoff = DateTimeOffset.UtcNow.AddHours(-hours).ToUnixTimeSeconds();
        var alerts = new List<WhaleAlert>();

        // Query recent transfers for watched addresses in parallel (max 3 concurrent to respect rate limits)
        var semaphore = new SemaphoreSlim(3);
        var tasks = WatchedAddresses.Take(8).Select(async kvp =>
        {
            await semaphore.WaitAsync();
            try
            {
                var transfers = await FetchRecentTransfersAsync(kvp.Key, kvp.Value, chainConfig, apiKey, cutoff);
                return transfers;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch transfers for {Label}", kvp.Value);
                return new List<WhaleAlert>();
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var batch in results)
            alerts.AddRange(batch);

        // Deduplicate by tx hash
        alerts = alerts
            .GroupBy(a => a.TxHash)
            .Select(g => g.First())
            .Where(a => a.EstimatedValueUsd == null || a.EstimatedValueUsd >= minValueUsd)
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .ToList();

        return new WhaleAlertResponse
        {
            Chain = chain,
            Hours = hours,
            MinValueUsd = minValueUsd,
            AlertCount = alerts.Count,
            Alerts = alerts,
            QueriedAt = DateTime.UtcNow
        };
    }

    private async Task<List<WhaleAlert>> FetchRecentTransfersAsync(
        string address, string label, ChainConfig chainConfig, string apiKey, long cutoffTimestamp)
    {
        var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=tokentx&address={address}&page=1&offset=50&sort=desc&apikey={apiKey}";
        var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return [];

        var alerts = new List<WhaleAlert>();

        foreach (var tx in result.EnumerateArray())
        {
            var timestampStr = tx.TryGetProperty("timeStamp", out var ts) ? ts.GetString() ?? "0" : "0";
            if (!long.TryParse(timestampStr, out var timestamp) || timestamp < cutoffTimestamp)
                continue;

            var from = tx.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
            var to = tx.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
            var tokenSymbol = tx.TryGetProperty("tokenSymbol", out var sym) ? sym.GetString() ?? "UNKNOWN" : "UNKNOWN";
            var contractAddress = tx.TryGetProperty("contractAddress", out var ca) ? ca.GetString() ?? "" : "";
            var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
            var valueStr = tx.TryGetProperty("value", out var v) ? v.GetString() ?? "0" : "0";
            var decimalsStr = tx.TryGetProperty("tokenDecimal", out var d) ? d.GetString() ?? "18" : "18";

            if (!int.TryParse(decimalsStr, out var decimals)) decimals = 18;

            decimal amount = 0;
            if (decimal.TryParse(valueStr, out var rawValue) && rawValue > 0)
                amount = rawValue / (decimal)Math.Pow(10, Math.Min(decimals, 18));

            // Estimate USD value for stablecoins (1:1) and known tokens
            decimal? estimatedValueUsd = tokenSymbol.ToUpperInvariant() switch
            {
                "USDC" or "USDT" or "DAI" or "BUSD" or "TUSD" or "USDP" or "PYUSD" or "GUSD" => amount,
                "WETH" or "ETH" => amount * 3000m, // rough estimate, updated by caller if needed
                "WBTC" => amount * 90000m,
                _ => amount > 1_000_000 ? amount : null // large raw amounts might be significant
            };

            // Identify the whale
            var isInbound = to?.ToLowerInvariant() == address.ToLowerInvariant();
            var fromLabel = !string.IsNullOrEmpty(from) && WatchedAddresses.TryGetValue(from, out var fl) ? fl : ShortenAddress(from ?? "");
            var toLabel = !string.IsNullOrEmpty(to) && WatchedAddresses.TryGetValue(to, out var tl) ? tl : ShortenAddress(to ?? "");

            alerts.Add(new WhaleAlert
            {
                TxHash = hash,
                From = from,
                FromLabel = fromLabel,
                To = to,
                ToLabel = toLabel,
                TokenSymbol = tokenSymbol,
                TokenAddress = contractAddress,
                Amount = amount,
                EstimatedValueUsd = estimatedValueUsd,
                Direction = isInbound ? "deposit" : "withdrawal",
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
            });
        }

        return alerts;
    }

    private static string ShortenAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 10) return address;
        return $"{address[..6]}...{address[^4..]}";
    }
}
