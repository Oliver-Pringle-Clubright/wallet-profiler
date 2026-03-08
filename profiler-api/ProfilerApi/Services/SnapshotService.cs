using System.Collections.Concurrent;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Stores portfolio snapshots in memory and provides historical data.
/// Snapshots are taken automatically when profiles are built.
/// </summary>
public class SnapshotService
{
    private readonly ConcurrentDictionary<string, List<PortfolioSnapshot>> _snapshots = new();
    private readonly ILogger<SnapshotService> _logger;

    private const int MaxSnapshotsPerAddress = 100;

    public SnapshotService(ILogger<SnapshotService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records a snapshot from a completed profile.
    /// Deduplicates by only storing one snapshot per hour per address.
    /// </summary>
    public void RecordSnapshot(WalletProfile profile)
    {
        var key = profile.Address.ToLowerInvariant();
        var snapshots = _snapshots.GetOrAdd(key, _ => new List<PortfolioSnapshot>());

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
        }
    }

    /// <summary>
    /// Returns historical snapshots for an address.
    /// </summary>
    public PortfolioHistory GetHistory(string address, int days = 30)
    {
        var key = address.ToLowerInvariant();
        if (!_snapshots.TryGetValue(key, out var snapshots))
            return new PortfolioHistory { Address = address };

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
}
