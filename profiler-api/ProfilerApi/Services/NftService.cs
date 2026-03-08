using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class NftService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<NftService> _logger;

    public NftService(HttpClient httpClient, IConfiguration config, ILogger<NftService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Fetches NFT holdings using Alchemy NFT API v3 (getNFTsForOwner) and floor prices.
    /// Available on standard+ tiers.
    /// </summary>
    public async Task<NftSummary> GetNftSummaryAsync(string rpcUrl, string address, decimal? ethPriceUsd)
    {
        try
        {
            // Alchemy NFT API v3 base URL is derived from the RPC URL
            // RPC: https://eth-mainnet.g.alchemy.com/v2/{key}
            // NFT: https://eth-mainnet.g.alchemy.com/nft/v3/{key}/getNFTsForOwner
            var nftBaseUrl = rpcUrl.Replace("/v2/", "/nft/v3/");

            var nfts = await GetNftsForOwnerAsync(nftBaseUrl, address);

            if (nfts.Count == 0)
                return new NftSummary();

            // Group by collection (contract address)
            var collections = nfts
                .GroupBy(n => n.ContractAddress.ToLowerInvariant())
                .Select(g => new
                {
                    ContractAddress = g.Key,
                    Name = g.First().CollectionName ?? "Unknown Collection",
                    Count = g.Count()
                })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList();

            // Fetch floor prices for top collections in parallel (max 5 concurrent)
            var semaphore = new SemaphoreSlim(5);
            var collectionTasks = collections.Select(async c =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var floorPrice = await GetFloorPriceAsync(nftBaseUrl, c.ContractAddress);
                    return new NftCollection
                    {
                        Name = c.Name,
                        ContractAddress = c.ContractAddress,
                        OwnedCount = c.Count,
                        FloorPriceEth = floorPrice,
                        FloorPriceUsd = floorPrice.HasValue && ethPriceUsd.HasValue
                            ? floorPrice.Value * ethPriceUsd.Value
                            : null
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var topCollections = (await Task.WhenAll(collectionTasks)).ToList();

            // Estimate total value from floor prices
            var estimatedValueEth = topCollections
                .Where(c => c.FloorPriceEth.HasValue)
                .Sum(c => c.FloorPriceEth!.Value * c.OwnedCount);

            return new NftSummary
            {
                TotalCount = nfts.Count,
                CollectionCount = nfts.Select(n => n.ContractAddress.ToLowerInvariant()).Distinct().Count(),
                EstimatedValueEth = estimatedValueEth > 0 ? estimatedValueEth : null,
                EstimatedValueUsd = estimatedValueEth > 0 && ethPriceUsd.HasValue
                    ? estimatedValueEth * ethPriceUsd.Value
                    : null,
                TopCollections = topCollections
                    .OrderByDescending(c => c.FloorPriceEth ?? 0)
                    .ThenByDescending(c => c.OwnedCount)
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch NFT summary for {Address}", address);
            return new NftSummary();
        }
    }

    private async Task<List<NftItem>> GetNftsForOwnerAsync(string nftBaseUrl, string address)
    {
        var url = $"{nftBaseUrl}/getNFTsForOwner?owner={address}&withMetadata=true&pageSize=100";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Alchemy NFT API returned {Status} for {Address}", response.StatusCode, address);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("ownedNfts", out var ownedNfts))
            return [];

        var items = new List<NftItem>();
        foreach (var nft in ownedNfts.EnumerateArray())
        {
            var contract = nft.TryGetProperty("contract", out var c)
                ? c.TryGetProperty("address", out var addr) ? addr.GetString() : null
                : null;

            if (string.IsNullOrEmpty(contract))
                continue;

            string? collectionName = null;
            if (nft.TryGetProperty("contract", out var contractObj))
            {
                if (contractObj.TryGetProperty("openSeaMetadata", out var osMeta) &&
                    osMeta.TryGetProperty("collectionName", out var cn))
                    collectionName = cn.GetString();
                else if (contractObj.TryGetProperty("name", out var nm))
                    collectionName = nm.GetString();
            }

            items.Add(new NftItem
            {
                ContractAddress = contract,
                CollectionName = collectionName
            });
        }

        return items;
    }

    private async Task<decimal?> GetFloorPriceAsync(string nftBaseUrl, string contractAddress)
    {
        try
        {
            var url = $"{nftBaseUrl}/getFloorPrice?contractAddress={contractAddress}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // Try OpenSea first, then LooksRare
            foreach (var marketplace in new[] { "openSea", "looksRare" })
            {
                if (doc.RootElement.TryGetProperty(marketplace, out var mp) &&
                    mp.TryGetProperty("floorPrice", out var fp))
                {
                    if (fp.ValueKind == JsonValueKind.Number)
                        return fp.GetDecimal();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get floor price for {Contract}", contractAddress);
            return null;
        }
    }

    private class NftItem
    {
        public string ContractAddress { get; set; } = string.Empty;
        public string? CollectionName { get; set; }
    }
}
