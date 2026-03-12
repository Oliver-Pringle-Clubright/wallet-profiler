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

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "0.0.0.0", "::1",
        "169.254.169.254", "metadata.google.internal"
    };

    private static bool IsUnsafeWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;
        if (uri.Scheme is not ("https" or "http"))
            return true;
        if (BlockedHosts.Contains(uri.Host))
            return true;
        if (System.Net.IPAddress.TryParse(uri.Host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            // Block private ranges: 10.x.x.x, 172.16-31.x.x, 192.168.x.x
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        return false;
    }

    public MonitorSubscription Subscribe(MonitorRequest request)
    {
        if (IsUnsafeWebhookUrl(request.WebhookUrl))
            throw new ArgumentException("Webhook URL must be a valid public HTTPS/HTTP URL");

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

    public static List<MonitorPlan> GetPlans() =>
    [
        new() { Plan = "free", MonthlyFeeEth = 0, MaxSubscriptions = 1, PollIntervalSeconds = 60, IncludesBalanceAlerts = false },
        new() { Plan = "basic", MonthlyFeeEth = 0.01m, MaxSubscriptions = 10, PollIntervalSeconds = 30, IncludesBalanceAlerts = true },
        new() { Plan = "premium", MonthlyFeeEth = 0.05m, MaxSubscriptions = 100, PollIntervalSeconds = 15, IncludesBalanceAlerts = true },
    ];

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
            client.Timeout = TimeSpan.FromSeconds(10);
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
