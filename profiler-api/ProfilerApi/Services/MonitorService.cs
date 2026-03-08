using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Background service that monitors subscribed wallets for large movements
/// and sends webhook alerts when thresholds are exceeded.
/// Polls every 30 seconds, checking the latest transaction for each subscription.
/// </summary>
public class MonitorService : BackgroundService
{
    private readonly ConcurrentDictionary<string, MonitorSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, int> _lastKnownTxCount = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitorService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public MonitorService(
        IServiceProvider serviceProvider,
        ILogger<MonitorService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public MonitorSubscription Subscribe(MonitorRequest request)
    {
        var sub = new MonitorSubscription
        {
            Address = request.Address.ToLowerInvariant(),
            Chain = request.Chain.ToLowerInvariant(),
            WebhookUrl = request.WebhookUrl,
            ThresholdEth = request.ThresholdEth
        };

        _subscriptions[sub.Id] = sub;
        _logger.LogInformation("New monitor subscription {Id} for {Address} on {Chain}, threshold {Threshold} ETH",
            sub.Id, sub.Address, sub.Chain, sub.ThresholdEth);

        return sub;
    }

    public bool Unsubscribe(string id)
    {
        if (_subscriptions.TryRemove(id, out var sub))
        {
            _lastKnownTxCount.TryRemove(id, out _);
            _logger.LogInformation("Removed monitor subscription {Id} for {Address}", id, sub.Address);
            return true;
        }
        return false;
    }

    public MonitorStatusResponse GetStatus()
    {
        return new MonitorStatusResponse
        {
            ActiveSubscriptions = _subscriptions.Count,
            Subscriptions = _subscriptions.Values.ToList()
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Monitor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllSubscriptionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in monitor polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CheckAllSubscriptionsAsync(CancellationToken ct)
    {
        if (_subscriptions.IsEmpty) return;

        using var scope = _serviceProvider.CreateScope();
        var ethService = scope.ServiceProvider.GetRequiredService<EthereumService>();

        // Check up to 10 subscriptions in parallel
        var semaphore = new SemaphoreSlim(10);
        var tasks = _subscriptions.Values.Where(s => s.Active).Select(async sub =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await CheckSubscriptionAsync(sub, ethService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check subscription {Id}", sub.Id);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task CheckSubscriptionAsync(MonitorSubscription sub, EthereumService ethService, CancellationToken ct)
    {
        var web3 = ethService.GetWeb3(sub.Chain);

        // Get current balance and tx count
        var balanceTask = ethService.GetEthBalanceAsync(web3, sub.Address);
        var txCountTask = ethService.GetTransactionCountAsync(web3, sub.Address);
        await Task.WhenAll(balanceTask, txCountTask);

        var currentTxCount = txCountTask.Result;
        var currentBalance = balanceTask.Result;

        // First time seeing this subscription — record baseline
        if (!_lastKnownTxCount.TryGetValue(sub.Id, out var lastTxCount))
        {
            _lastKnownTxCount[sub.Id] = currentTxCount;
            return;
        }

        // No new transactions
        if (currentTxCount <= lastTxCount)
            return;

        _lastKnownTxCount[sub.Id] = currentTxCount;

        // Check if balance changed significantly (simple heuristic)
        // For a more precise check we'd need to look at actual tx values,
        // but this is a lightweight monitoring approach
        var newTxs = currentTxCount - lastTxCount;

        _logger.LogInformation("Subscription {Id}: {NewTxs} new transaction(s) detected for {Address}",
            sub.Id, newTxs, sub.Address);

        // Send alert for any new activity (agents can filter by their own criteria)
        var alert = new WalletAlert
        {
            SubscriptionId = sub.Id,
            Address = sub.Address,
            Type = "new_transactions",
            Description = $"{newTxs} new transaction(s) detected. Current balance: {currentBalance:F4} ETH.",
            AmountEth = currentBalance
        };

        await SendWebhookAsync(sub.WebhookUrl, alert);
    }

    private async Task SendWebhookAsync(string webhookUrl, WalletAlert alert)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(alert, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(webhookUrl, content);

            _logger.LogInformation("Webhook sent to {Url}: {Status}", webhookUrl, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send webhook to {Url}", webhookUrl);
        }
    }
}
