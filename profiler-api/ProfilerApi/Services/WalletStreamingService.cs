using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using ProfilerApi.Hubs;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Background service that monitors subscribed wallet addresses and broadcasts
/// real-time updates via SignalR when changes are detected (balance changes,
/// new transactions, risk level changes).
/// </summary>
public class WalletStreamingService : BackgroundService
{
    private readonly IHubContext<WalletHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WalletStreamingService> _logger;

    // Track last-known state per address for change detection
    private readonly ConcurrentDictionary<string, WalletSnapshot> _snapshots = new();
    // Track which addresses have active subscribers (group membership)
    private readonly ConcurrentDictionary<string, int> _subscriberCounts = new();

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public WalletStreamingService(
        IHubContext<WalletHub> hubContext,
        IServiceProvider serviceProvider,
        ILogger<WalletStreamingService> logger)
    {
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client subscribes to an address via the SignalR hub.
    /// Adds the address to the polling list.
    /// </summary>
    public void TrackAddress(string address)
    {
        var normalized = address.ToLowerInvariant();
        _subscriberCounts.AddOrUpdate(normalized, 1, (_, count) => count + 1);
        _logger.LogInformation("Tracking address {Address} ({Count} subscribers)", normalized, _subscriberCounts[normalized]);
    }

    /// <summary>
    /// Called when a client unsubscribes. Removes from polling if no subscribers remain.
    /// </summary>
    public void UntrackAddress(string address)
    {
        var normalized = address.ToLowerInvariant();
        if (_subscriberCounts.TryGetValue(normalized, out var count))
        {
            if (count <= 1)
            {
                _subscriberCounts.TryRemove(normalized, out _);
                _snapshots.TryRemove(normalized, out _);
            }
            else
            {
                _subscriberCounts[normalized] = count - 1;
            }
        }
    }

    public int ActiveSubscriptions => _subscriberCounts.Count;
    public IReadOnlyCollection<string> TrackedAddresses => _subscriberCounts.Keys.ToList().AsReadOnly();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wallet streaming service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllAddressesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in wallet streaming poll loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollAllAddressesAsync(CancellationToken ct)
    {
        var addresses = _subscriberCounts.Keys.ToList();
        if (addresses.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var ethService = scope.ServiceProvider.GetRequiredService<EthereumService>();

        var semaphore = new SemaphoreSlim(5);
        var tasks = addresses.Select(async address =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await PollAddressAsync(address, ethService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll {Address}", address);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task PollAddressAsync(string address, EthereumService ethService, CancellationToken ct)
    {
        var web3 = ethService.GetWeb3("ethereum");

        var balanceTask = ethService.GetEthBalanceAsync(web3, address);
        var txCountTask = ethService.GetTransactionCountAsync(web3, address);
        await Task.WhenAll(balanceTask, txCountTask);

        var currentBalance = balanceTask.Result;
        var currentTxCount = txCountTask.Result;

        if (!_snapshots.TryGetValue(address, out var lastSnapshot))
        {
            // First poll — record baseline, no notification
            _snapshots[address] = new WalletSnapshot(currentBalance, currentTxCount);
            return;
        }

        var updates = new List<WalletUpdate>();

        // Check for balance change (> 0.001 ETH threshold)
        var balanceDelta = currentBalance - lastSnapshot.EthBalance;
        if (Math.Abs(balanceDelta) > 0.001m)
        {
            updates.Add(new WalletUpdate
            {
                Address = address,
                EventType = "balance_change",
                Description = $"ETH balance changed by {balanceDelta:+0.####;-0.####} ETH (now {currentBalance:F4} ETH)",
                Data = new { previousBalance = lastSnapshot.EthBalance, currentBalance, delta = balanceDelta }
            });
        }

        // Check for new transactions
        if (currentTxCount > lastSnapshot.TransactionCount)
        {
            var newTxs = currentTxCount - lastSnapshot.TransactionCount;
            updates.Add(new WalletUpdate
            {
                Address = address,
                EventType = "new_transaction",
                Description = $"{newTxs} new transaction(s) detected",
                Data = new { newTransactions = newTxs, totalTransactions = currentTxCount }
            });
        }

        // Update snapshot
        _snapshots[address] = new WalletSnapshot(currentBalance, currentTxCount);

        // Broadcast updates to the address group
        foreach (var update in updates)
        {
            await _hubContext.Clients.Group(address).SendAsync("WalletUpdate", update, ct);
            _logger.LogInformation("Broadcast {EventType} for {Address}", update.EventType, address);
        }
    }

    private record WalletSnapshot(decimal EthBalance, int TransactionCount);
}
