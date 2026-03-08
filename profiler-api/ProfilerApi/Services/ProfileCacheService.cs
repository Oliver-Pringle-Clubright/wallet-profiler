using Microsoft.Extensions.Caching.Memory;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class ProfileCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProfileCacheService> _logger;

    // Cache durations
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EnsTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan PriceTtl = TimeSpan.FromMinutes(1);

    public ProfileCacheService(IMemoryCache cache, ILogger<ProfileCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // --- Full profile cache ---

    public WalletProfile? GetProfile(string address, string chain, string tier)
    {
        var key = ProfileKey(address, chain, tier);
        if (_cache.TryGetValue(key, out WalletProfile? profile))
        {
            _logger.LogInformation("Cache HIT for profile {Address} ({Chain}/{Tier})", address, chain, tier);
            return profile;
        }
        return null;
    }

    public void SetProfile(string address, string chain, string tier, WalletProfile profile)
    {
        var key = ProfileKey(address, chain, tier);
        _cache.Set(key, profile, ProfileTtl);
    }

    // --- ENS cache ---

    public bool TryGetEns(string address, out string? ensName)
    {
        var key = $"ens:{address.ToLowerInvariant()}";
        return _cache.TryGetValue(key, out ensName);
    }

    public void SetEns(string address, string? ensName)
    {
        var key = $"ens:{address.ToLowerInvariant()}";
        _cache.Set(key, ensName, EnsTtl);
    }

    public bool TryGetEnsReverse(string ensName, out string? address)
    {
        var key = $"ens-reverse:{ensName.ToLowerInvariant()}";
        return _cache.TryGetValue(key, out address);
    }

    public void SetEnsReverse(string ensName, string address)
    {
        var key = $"ens-reverse:{ensName.ToLowerInvariant()}";
        _cache.Set(key, address, EnsTtl);
    }

    // --- Price cache ---

    public bool TryGetPrices(string cacheKey, out (decimal? EthPrice, Dictionary<string, decimal> TokenPrices) prices)
    {
        var key = $"prices:{cacheKey}";
        if (_cache.TryGetValue(key, out (decimal? EthPrice, Dictionary<string, decimal> TokenPrices) cached))
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
        _cache.Set(key, (ethPrice, tokenPrices), PriceTtl);
    }

    private static string ProfileKey(string address, string chain, string tier)
        => $"profile:{address.ToLowerInvariant()}:{chain}:{tier}";
}
