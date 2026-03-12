using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Stores portfolio snapshots with Redis persistence (when available)
/// and in-memory fallback. Snapshots survive container restarts when Redis is configured.
/// </summary>
public class SnapshotService
{
    private readonly ConcurrentDictionary<string, List<PortfolioSnapshot>> _snapshots = new();
    private readonly IDistributedCache? _distCache;
    private readonly ILogger<SnapshotService> _logger;
    private readonly bool _useRedis;

    private const int MaxSnapshotsPerAddress = 200;
    private static readonly TimeSpan RedisTtl = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SnapshotService(ILogger<SnapshotService> logger, IDistributedCache? distCache = null, IConfiguration? config = null)
    {
        _logger = logger;
        _distCache = distCache;
        _useRedis = !string.IsNullOrEmpty(config?["Redis:ConnectionString"]);
        if (_useRedis)
            _logger.LogInformation("Snapshot persistence: Redis (90-day TTL)");
        else
            _logger.LogInformation("Snapshot persistence: in-memory only");
    }

    /// <summary>
    /// Records a snapshot from a completed profile.
    /// Deduplicates by only storing one snapshot per hour per address.
    /// Persists to Redis when available.
    /// </summary>
    public void RecordSnapshot(WalletProfile profile)
    {
        var key = profile.Address.ToLowerInvariant();
        var snapshots = _snapshots.GetOrAdd(key, _ => LoadFromRedis(key));

        lock (snapshots)
        {
            // Deduplicate: skip if last snapshot was less than 1 hour ago
            if (snapshots.Count > 0)
            {
                var last = snapshots[^1];
                if ((DateTime.UtcNow - last.SnapshotAt).TotalHours < 1)
                    return;
            }

            snapshots.Add(new PortfolioSnapshot
            {
                Address = profile.Address,
                TotalValueUsd = profile.TotalValueUsd,
                EthBalance = profile.EthBalance,
                TokenCount = profile.TopTokens.Count(t => !t.IsSpam),
                TransactionCount = profile.TransactionCount,
                SnapshotAt = DateTime.UtcNow
            });

            // Cap stored snapshots
            while (snapshots.Count > MaxSnapshotsPerAddress)
                snapshots.RemoveAt(0);

            // Persist to Redis
            SaveToRedis(key, snapshots);
        }
    }

    /// <summary>
    /// Returns historical snapshots for an address.
    /// </summary>
    public PortfolioHistory GetHistory(string address, int days = 30)
    {
        var key = address.ToLowerInvariant();
        var snapshots = _snapshots.GetOrAdd(key, _ => LoadFromRedis(key));

        List<PortfolioSnapshot> filtered;
        lock (snapshots)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            filtered = snapshots.Where(s => s.SnapshotAt >= cutoff).ToList();
        }

        if (filtered.Count == 0)
            return new PortfolioHistory { Address = address };

        var current = filtered.Last();
        var oldest = filtered.First();

        decimal? changePct = null;
        if (current.TotalValueUsd.HasValue && oldest.TotalValueUsd.HasValue && oldest.TotalValueUsd > 0)
        {
            changePct = Math.Round(
                (current.TotalValueUsd.Value / oldest.TotalValueUsd.Value - 1) * 100, 2);
        }

        return new PortfolioHistory
        {
            Address = address,
            SnapshotCount = filtered.Count,
            CurrentValueUsd = current.TotalValueUsd,
            OldestValueUsd = oldest.TotalValueUsd,
            ValueChangePct = changePct,
            Snapshots = filtered
        };
    }

    private List<PortfolioSnapshot> LoadFromRedis(string addressKey)
    {
        if (!_useRedis || _distCache == null)
            return new List<PortfolioSnapshot>();

        try
        {
            var json = _distCache.GetString($"snapshots:{addressKey}");
            if (!string.IsNullOrEmpty(json))
            {
                var loaded = JsonSerializer.Deserialize<List<PortfolioSnapshot>>(json, JsonOpts);
                if (loaded != null)
                {
                    _logger.LogDebug("Loaded {Count} snapshots from Redis for {Address}", loaded.Count, addressKey);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load snapshots from Redis for {Address}", addressKey);
        }

        return new List<PortfolioSnapshot>();
    }

    private void SaveToRedis(string addressKey, List<PortfolioSnapshot> snapshots)
    {
        if (!_useRedis || _distCache == null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(snapshots, JsonOpts);
            _distCache.SetString($"snapshots:{addressKey}", json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = RedisTtl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist snapshots to Redis for {Address}", addressKey);
        }
    }
}
