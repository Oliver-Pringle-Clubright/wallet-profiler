using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class PriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PriceService> _logger;
    private readonly ProfileCacheService _cache;

    private static readonly Dictionary<string, string> LlamaPlatforms = new()
    {
        ["ethereum"] = "ethereum",
        ["base"] = "base",
        ["arbitrum"] = "arbitrum"
    };

    public PriceService(HttpClient httpClient, ILogger<PriceService> logger, ProfileCacheService cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<(decimal? EthPrice, Dictionary<string, decimal> TokenPrices)> GetAllPricesAsync(
        List<string> contractAddresses, string chain = "ethereum")
    {
        if (!LlamaPlatforms.TryGetValue(chain, out var platform))
            return (null, new Dictionary<string, decimal>());

        // Build cache key from sorted addresses
        var cacheKey = $"{chain}:{string.Join(",", contractAddresses.OrderBy(a => a).Select(a => a.ToLowerInvariant()))}";
        if (_cache.TryGetPrices(cacheKey, out var cached))
        {
            _logger.LogInformation("Cache HIT for prices ({Count} tokens)", contractAddresses.Count);
            return cached;
        }

        try
        {
            var coins = new List<string> { "coingecko:ethereum" };
            coins.AddRange(contractAddresses.Select(a => $"{platform}:{a.ToLowerInvariant()}"));
            var coinList = string.Join(",", coins);

            var url = $"https://coins.llama.fi/prices/current/{coinList}";
            var json = await _httpClient.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            decimal? ethPrice = null;
            var tokenPrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (!doc.RootElement.TryGetProperty("coins", out var coinsElement))
                return (null, tokenPrices);

            foreach (var prop in coinsElement.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("price", out var priceElement))
                    continue;

                var price = priceElement.GetDecimal();

                if (prop.Name == "coingecko:ethereum")
                {
                    ethPrice = price;
                }
                else
                {
                    var colonIndex = prop.Name.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        var address = prop.Name[(colonIndex + 1)..];
                        tokenPrices[address] = price;
                    }
                }
            }

            _cache.SetPrices(cacheKey, ethPrice, tokenPrices);
            return (ethPrice, tokenPrices);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch prices from DeFi Llama");
            return (null, new Dictionary<string, decimal>());
        }
    }

    public async Task<decimal?> EnrichWithPricesAsync(List<TokenBalance> tokens, string chain = "ethereum")
    {
        if (tokens.Count == 0)
        {
            var (ethOnly, _) = await GetAllPricesAsync([], chain);
            return ethOnly;
        }

        var addresses = tokens.Select(t => t.ContractAddress).ToList();
        var (ethPrice, tokenPrices) = await GetAllPricesAsync(addresses, chain);

        foreach (var token in tokens)
        {
            if (tokenPrices.TryGetValue(token.ContractAddress, out var price))
            {
                token.PriceUsd = price;
                token.ValueUsd = token.Balance * price;
            }
        }

        return ethPrice;
    }
}
