using System.Text.Json;
using System.Text.Json.Serialization;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Fetches ERC-20 token transfer history from Etherscan V2 and builds
/// a timeline with inbound/outbound flow analysis.
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
            var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=tokentx&address={address}&startblock=0&endblock=99999999&page=1&offset=200&sort=desc&apikey={apiKey}";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                return new TransferHistory();

            var transfers = new List<TokenTransfer>();
            var addressLower = address.ToLowerInvariant();

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

                // Lookup USD value if we have prices
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
                RecentTransfers = transfers.Take(20).ToList(),
                Timeline = timeline
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch transfer history for {Address} on {Chain}", address, chain);
            return new TransferHistory();
        }
    }
}
