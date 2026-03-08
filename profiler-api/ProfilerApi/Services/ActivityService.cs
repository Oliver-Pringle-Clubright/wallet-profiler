using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class ActivityService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(HttpClient httpClient, IConfiguration config, ILogger<ActivityService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<WalletActivity> GetActivityAsync(string address, string chain = "ethereum")
    {
        if (!ChainConfig.Chains.TryGetValue(chain, out var chainConfig))
            return new WalletActivity();

        var apiKey = _config["Etherscan:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return new WalletActivity();

        try
        {
            var url = $"{ChainConfig.EtherscanV2BaseUrl}?chainid={chainConfig.ChainId}&module=account&action=txlist&address={address}&startblock=0&endblock=99999999&page=1&offset=1000&sort=asc&apikey={apiKey}";
            var response = await _httpClient.GetFromJsonAsync<EtherscanResponse<TxDto>>(url);

            if (response?.Result is null || !response.IsSuccess || response.Result.Count == 0)
                return new WalletActivity();

            var txs = response.Result;
            var firstTx = DateTimeOffset.FromUnixTimeSeconds(long.Parse(txs.First().TimeStamp!)).UtcDateTime;
            var lastTx = DateTimeOffset.FromUnixTimeSeconds(long.Parse(txs.Last().TimeStamp!)).UtcDateTime;

            var uniqueAddresses = txs
                .SelectMany(tx => new[] { tx.To, tx.From })
                .Where(a => !string.IsNullOrEmpty(a) && !a.Equals(address, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var activeDays = txs
                .Select(tx => DateTimeOffset.FromUnixTimeSeconds(long.Parse(tx.TimeStamp!)).Date)
                .Distinct()
                .Count();

            return new WalletActivity
            {
                FirstTransaction = firstTx,
                LastTransaction = lastTx,
                DaysActive = activeDays,
                UniqueInteractions = uniqueAddresses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch activity for {Address} on {Chain}", address, chain);
            return new WalletActivity();
        }
    }

    private class TxDto
    {
        [JsonPropertyName("timeStamp")]
        public string? TimeStamp { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }
    }
}
