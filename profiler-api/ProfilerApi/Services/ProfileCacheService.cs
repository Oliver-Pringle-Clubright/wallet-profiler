using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Caching service with tiered TTLs. Supports both in-memory (default)
/// and Redis (when Redis:ConnectionString is configured) backends.
/// </summary>
public class ProfileCacheService
{
    private readonly IMemoryCache _memCache;
    private readonly IDistributedCache? _distCache;
    private readonly ILogger<ProfileCacheService> _logger;
    private readonly bool _useRedis;

    // Cache durations
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EnsTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan PriceTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TokenMetadataTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NftFloorPriceTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProfileCacheService(
        IMemoryCache cache,
        ILogger<ProfileCacheService> logger,
        IDistributedCache? distCache = null,
        IConfiguration? config = null)
    {
        _memCache = cache;
        _logger = logger;
        _distCache = distCache;

        // Only use Redis if explicitly configured
        _useRedis = !string.IsNullOrEmpty(config?["Redis:ConnectionString"]);
        if (_useRedis)
            _logger.LogInformation("Redis cache enabled");
    }

    public string CacheBackend => _useRedis ? "redis" : "memory";

    // --- Full profile cache ---

    public WalletProfile? GetProfile(string address, string chain, string tier)
    {
        var key = ProfileKey(address, chain, tier);

        // Try memory cache first (L1)
        if (_memCache.TryGetValue(key, out WalletProfile? profile))
        {
            _logger.LogInformation("Cache HIT (memory) for profile {Address} ({Chain}/{Tier})", address, chain, tier);
            return profile;
        }

        // Try Redis (L2) if enabled
        if (_useRedis && _distCache != null)
        {
            try
            {
                var json = _distCache.GetString(key);
                if (json != null)
                {
                    profile = JsonSerializer.Deserialize<WalletProfile>(json, JsonOpts);
                    if (profile != null)
                    {
                        // Promote to L1
                        _memCache.Set(key, profile, ProfileTtl);
                        _logger.LogInformation("Cache HIT (redis) for profile {Address} ({Chain}/{Tier})", address, chain, tier);
                        return profile;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis GET failed for {Key}, falling back to memory", key);
            }
        }

        return null;
    }

    public void SetProfile(string address, string chain, string tier, WalletProfile profile)
    {
        var key = ProfileKey(address, chain, tier);

        // Always set in memory (L1)
        _memCache.Set(key, profile, ProfileTtl);

        // Also set in Redis (L2) if enabled
        if (_useRedis && _distCache != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(profile, JsonOpts);
                _distCache.SetString(key, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ProfileTtl
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SET failed for {Key}", key);
            }
        }
    }

    // --- ENS cache (memory only — fast, small) ---

    public bool TryGetEns(string address, out string? ensName)
    {
        var key = $"ens:{address.ToLowerInvariant()}";
        return _memCache.TryGetValue(key, out ensName);
    }

    public void SetEns(string address, string? ensName)
    {
        var key = $"ens:{address.ToLowerInvariant()}";
        _memCache.Set(key, ensName, EnsTtl);
    }

    public bool TryGetEnsReverse(string ensName, out string? address)
    {
        var key = $"ens-reverse:{ensName.ToLowerInvariant()}";
        return _memCache.TryGetValue(key, out address);
    }

    public void SetEnsReverse(string ensName, string address)
    {
        var key = $"ens-reverse:{ensName.ToLowerInvariant()}";
        _memCache.Set(key, address, EnsTtl);
    }

    // --- Price cache (memory only — volatile, 1 min TTL) ---

    public bool TryGetPrices(string cacheKey, out (decimal? EthPrice, Dictionary<string, decimal> TokenPrices) prices)
    {
        var key = $"prices:{cacheKey}";
        if (_memCache.TryGetValue(key, out (decimal? EthPrice, Dictionary<string, decimal> TokenPrices) cached))
        {
            prices = cached;
            return true;
        }
        prices = default;
        return false;
    }

    public void SetPrices(string cacheKey, decimal? ethPrice, Dictionary<string, decimal> tokenPrices)
    {
        var key = $"prices:{cacheKey}";
        _memCache.Set(key, (ethPrice, tokenPrices), PriceTtl);
    }

    // --- Token metadata cache (memory only — stable data, 24h TTL) ---

    public bool TryGetTokenMetadata(string contractAddress, out (string? Symbol, int Decimals) metadata)
    {
        var key = $"token-meta:{contractAddress.ToLowerInvariant()}";
        if (_memCache.TryGetValue(key, out (string? Symbol, int Decimals) cached))
        {
            metadata = cached;
            return true;
        }
        metadata = default;
        return false;
    }

    public void SetTokenMetadata(string contractAddress, string? symbol, int decimals)
    {
        var key = $"token-meta:{contractAddress.ToLowerInvariant()}";
        _memCache.Set(key, (symbol, decimals), TokenMetadataTtl);
    }

    // --- NFT floor price cache (memory only — 1h TTL) ---

    public bool TryGetNftFloorPrice(string contractAddress, out decimal? floorPrice)
    {
        var key = $"nft-floor:{contractAddress.ToLowerInvariant()}";
        if (_memCache.TryGetValue(key, out decimal? cached))
        {
            floorPrice = cached;
            return true;
        }
        floorPrice = null;
        return false;
    }

    public void SetNftFloorPrice(string contractAddress, decimal? floorPrice)
    {
        var key = $"nft-floor:{contractAddress.ToLowerInvariant()}";
        _memCache.Set(key, floorPrice, NftFloorPriceTtl);
    }

    private static string ProfileKey(string address, string chain, string tier)
        => $"profile:{address.ToLowerInvariant()}:{chain}:{tier}";
}
