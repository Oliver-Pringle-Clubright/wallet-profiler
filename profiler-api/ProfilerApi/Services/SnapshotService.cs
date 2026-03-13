using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ProfilerApi.Data;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Stores portfolio snapshots with PostgreSQL persistence (preferred),
/// Redis L2 cache, and in-memory fallback.
/// Priority: PostgreSQL > Redis > In-memory.
/// </summary>
public class SnapshotService
{
    private readonly ConcurrentDictionary<string, List<PortfolioSnapshot>> _snapshots = new();
    private readonly IDistributedCache? _distCache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SnapshotService> _logger;
    private readonly bool _useRedis;
    private readonly bool _usePostgres;

    private const int MaxSnapshotsPerAddress = 200;
    private static readonly TimeSpan RedisTtl = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SnapshotService(
        ILogger<SnapshotService> logger,
        IServiceProvider serviceProvider,
        IDistributedCache? distCache = null,
        IConfiguration? config = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _distCache = distCache;
        _useRedis = !string.IsNullOrEmpty(config?["Redis:ConnectionString"]);
        _usePostgres = !string.IsNullOrEmpty(config?["ConnectionStrings:ProfilerDb"]);

        if (_usePostgres)
            _logger.LogInformation("Snapshot persistence: PostgreSQL");
        else if (_useRedis)
            _logger.LogInformation("Snapshot persistence: Redis (90-day TTL)");
        else
            _logger.LogInformation("Snapshot persistence: in-memory only");
    }

    public void RecordSnapshot(WalletProfile profile)
    {
        var key = profile.Address.ToLowerInvariant();
        var snapshots = _snapshots.GetOrAdd(key, _ => LoadSnapshots(key));

        lock (snapshots)
        {
            if (snapshots.Count > 0)
            {
                var last = snapshots[^1];
                if ((DateTime.UtcNow - last.SnapshotAt).TotalHours < 1)
                    return;
            }

            var snapshot = new PortfolioSnapshot
            {
                Address = profile.Address,
                TotalValueUsd = profile.TotalValueUsd,
                EthBalance = profile.EthBalance,
                TokenCount = profile.TopTokens.Count(t => !t.IsSpam),
                TransactionCount = profile.TransactionCount,
                SnapshotAt = DateTime.UtcNow
            };

            snapshots.Add(snapshot);

            while (snapshots.Count > MaxSnapshotsPerAddress)
                snapshots.RemoveAt(0);

            // Persist
            if (_usePostgres)
                SaveToPostgres(snapshot);
            else
                SaveToRedis(key, snapshots);
        }
    }

    public PortfolioHistory GetHistory(string address, int days = 30)
    {
        var key = address.ToLowerInvariant();
        var snapshots = _snapshots.GetOrAdd(key, _ => LoadSnapshots(key));

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

    private List<PortfolioSnapshot> LoadSnapshots(string addressKey)
    {
        if (_usePostgres)
            return LoadFromPostgres(addressKey);
        if (_useRedis)
            return LoadFromRedis(addressKey);
        return new List<PortfolioSnapshot>();
    }

    private List<PortfolioSnapshot> LoadFromPostgres(string addressKey)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProfilerDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-90);

            return db.Snapshots
                .Where(s => s.Address == addressKey && s.SnapshotAt >= cutoff)
                .OrderBy(s => s.SnapshotAt)
                .Take(MaxSnapshotsPerAddress)
                .Select(s => new PortfolioSnapshot
                {
                    Address = s.Address,
                    TotalValueUsd = s.TotalValueUsd,
                    EthBalance = s.EthBalance,
                    TokenCount = s.TokenCount,
                    TransactionCount = s.TransactionCount,
                    SnapshotAt = s.SnapshotAt
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load snapshots from PostgreSQL for {Address}", addressKey);
            return new List<PortfolioSnapshot>();
        }
    }

    private void SaveToPostgres(PortfolioSnapshot snapshot)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProfilerDbContext>();
            db.Snapshots.Add(new SnapshotEntity
            {
                Address = snapshot.Address.ToLowerInvariant(),
                TotalValueUsd = snapshot.TotalValueUsd,
                EthBalance = snapshot.EthBalance,
                TokenCount = snapshot.TokenCount,
                TransactionCount = snapshot.TransactionCount,
                SnapshotAt = snapshot.SnapshotAt
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save snapshot to PostgreSQL for {Address}", snapshot.Address);
        }
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
                    return loaded;
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
