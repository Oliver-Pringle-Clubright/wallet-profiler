using System.Text.Json;
using System.Text.Json.Serialization;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Fetches ERC-20 token transfer history AND native ETH transfers from
/// Etherscan V2 and builds a unified timeline with inbound/outbound flow analysis.
/// </summary>
public class TransferHistoryService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<TransferHistoryService> _logger;

    public TransferHistoryService(HttpClient httpClient, IConfiguration config, ILogger<TransferHistoryService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<TransferHistory> GetTransferHistoryAsync(string address, string chain = "ethereum", Dictionary<string, decimal>? tokenPrices = null)
    {
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return new TransferHistory();

        var apiKey = _config["Etherscan:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return new TransferHistory();

        try
        {
            // Fetch ERC-20 token transfers AND native ETH transfers in parallel
            var tokenTxUrl = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=tokentx&address={address}&startblock=0&endblock=99999999&page=1&offset=200&sort=desc&apikey={apiKey}";
            var ethTxUrl = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=txlist&address={address}&startblock=0&endblock=99999999&page=1&offset=200&sort=desc&apikey={apiKey}";

            var tokenTask = _httpClient.GetStringAsync(tokenTxUrl);
            var ethTask = _httpClient.GetStringAsync(ethTxUrl);

            await Task.WhenAll(tokenTask, ethTask);

            var transfers = new List<TokenTransfer>();
            var addressLower = address.ToLowerInvariant();

            // --- Parse ERC-20 token transfers ---
            ParseTokenTransfers(tokenTask.Result, addressLower, tokenPrices, transfers);

            // --- Parse native ETH transfers ---
            ParseEthTransfers(ethTask.Result, addressLower, chain, tokenPrices, transfers);

            // Sort all transfers by timestamp descending
            transfers = transfers.OrderByDescending(t => t.Timestamp).ToList();

            // Build monthly timeline
            var timeline = transfers
                .GroupBy(t => t.Timestamp.ToString("yyyy-MM"))
                .OrderByDescending(g => g.Key)
                .Take(12)
                .Select(g => new TransferPeriod
                {
                    Period = g.Key,
                    InboundCount = g.Count(t => t.Direction == "in"),
                    OutboundCount = g.Count(t => t.Direction == "out"),
                    InboundValueUsd = g.Where(t => t.Direction == "in" && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value),
                    OutboundValueUsd = g.Where(t => t.Direction == "out" && t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value)
                })
                .ToList();

            var inbound = transfers.Where(t => t.Direction == "in").ToList();
            var outbound = transfers.Where(t => t.Direction == "out").ToList();
            var inboundUsd = inbound.Where(t => t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);
            var outboundUsd = outbound.Where(t => t.ValueUsd.HasValue).Sum(t => t.ValueUsd!.Value);

            return new TransferHistory
            {
                TotalTransfers = transfers.Count,
                InboundCount = inbound.Count,
                OutboundCount = outbound.Count,
                NetFlowUsd = (inboundUsd - outboundUsd) != 0 ? inboundUsd - outboundUsd : null,
                RecentTransfers = transfers.Take(25).ToList(),
                Timeline = timeline
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch transfer history for {Address} on {Chain}", address, chain);
            return new TransferHistory();
        }
    }

    private void ParseTokenTransfers(string json, string addressLower, Dictionary<string, decimal>? tokenPrices, List<TokenTransfer> transfers)
    {
        var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return;

        foreach (var tx in result.EnumerateArray())
        {
            var from = tx.TryGetProperty("from", out var f) ? f.GetString() : null;
            var to = tx.TryGetProperty("to", out var t) ? t.GetString() : null;
            var tokenSymbol = tx.TryGetProperty("tokenSymbol", out var ts) ? ts.GetString() ?? "UNKNOWN" : "UNKNOWN";
            var contractAddress = tx.TryGetProperty("contractAddress", out var ca) ? ca.GetString() ?? "" : "";
            var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
            var valueStr = tx.TryGetProperty("value", out var v) ? v.GetString() ?? "0" : "0";
            var decimalsStr = tx.TryGetProperty("tokenDecimal", out var d) ? d.GetString() ?? "18" : "18";
            var timestampStr = tx.TryGetProperty("timeStamp", out var tsVal) ? tsVal.GetString() ?? "0" : "0";

            if (!int.TryParse(decimalsStr, out var decimals)) decimals = 18;
            if (!long.TryParse(timestampStr, out var timestamp)) continue;

            var isInbound = to?.ToLowerInvariant() == addressLower;
            var counterparty = isInbound ? (from ?? "") : (to ?? "");

            decimal amount = 0;
            if (decimal.TryParse(valueStr, out var rawValue) && rawValue > 0)
            {
                amount = rawValue / (decimal)Math.Pow(10, Math.Min(decimals, 18));
            }

            decimal? valueUsd = null;
            if (tokenPrices != null && amount > 0)
            {
                var priceKey = contractAddress.ToLowerInvariant();
                if (tokenPrices.TryGetValue(priceKey, out var price))
                    valueUsd = amount * price;
            }

            transfers.Add(new TokenTransfer
            {
                TxHash = hash,
                TokenSymbol = tokenSymbol,
                TokenAddress = contractAddress,
                Direction = isInbound ? "in" : "out",
                Counterparty = counterparty,
                Amount = amount,
                ValueUsd = valueUsd,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
            });
        }
    }

    private void ParseEthTransfers(string json, string addressLower, string chain, Dictionary<string, decimal>? tokenPrices, List<TokenTransfer> transfers)
    {
        var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return;

        var nativeSymbol = ChainConfig.GetNativeSymbol(chain);
        // Look up native token price from tokenPrices or use ETH key
        decimal? nativePrice = null;
        if (tokenPrices != null)
        {
            // Try common keys for native token
            if (tokenPrices.TryGetValue("native", out var np))
                nativePrice = np;
            else if (tokenPrices.TryGetValue("0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", out var ep))
                nativePrice = ep;
        }

        // Track seen hashes to avoid duplicates with token transfers
        var seenHashes = new HashSet<string>(transfers.Select(t => t.TxHash), StringComparer.OrdinalIgnoreCase);

        foreach (var tx in result.EnumerateArray())
        {
            var valueStr = tx.TryGetProperty("value", out var v) ? v.GetString() ?? "0" : "0";
            if (!decimal.TryParse(valueStr, out var rawValue) || rawValue == 0)
                continue; // Skip zero-value transactions (contract calls without ETH transfer)

            var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
            var from = tx.TryGetProperty("from", out var f) ? f.GetString() : null;
            var to = tx.TryGetProperty("to", out var t) ? t.GetString() : null;
            var timestampStr = tx.TryGetProperty("timeStamp", out var tsVal) ? tsVal.GetString() ?? "0" : "0";
            var isError = tx.TryGetProperty("isError", out var err) ? err.GetString() : "0";

            if (isError == "1") continue; // Skip failed transactions
            if (!long.TryParse(timestampStr, out var timestamp)) continue;

            var isInbound = to?.ToLowerInvariant() == addressLower;
            var counterparty = isInbound ? (from ?? "") : (to ?? "");

            var amount = rawValue / 1_000_000_000_000_000_000m; // wei to ETH

            decimal? valueUsd = nativePrice.HasValue ? amount * nativePrice.Value : null;

            transfers.Add(new TokenTransfer
            {
                TxHash = hash,
                TokenSymbol = nativeSymbol,
                TokenAddress = "native",
                Direction = isInbound ? "in" : "out",
                Counterparty = counterparty,
                Amount = amount,
                ValueUsd = valueUsd,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
            });
        }
    }
}
