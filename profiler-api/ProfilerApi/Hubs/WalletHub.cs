using Microsoft.AspNetCore.SignalR;

namespace ProfilerApi.Hubs;

public class WalletHub : Hub
{
    private readonly ILogger<WalletHub> _logger;

    public WalletHub(ILogger<WalletHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client subscribes to real-time updates for a wallet address.
    /// Joins the SignalR group named after the wallet address.
    /// </summary>
    public async Task Subscribe(string address)
    {
        var normalizedAddress = address.Trim().ToLowerInvariant();
        await Groups.AddToGroupAsync(Context.ConnectionId, normalizedAddress);
        _logger.LogInformation("Client {ConnectionId} subscribed to {Address}", Context.ConnectionId, normalizedAddress);
        await Clients.Caller.SendAsync("Subscribed", new { address = normalizedAddress, message = "Subscribed to wallet updates" });
    }

    /// <summary>
    /// Client unsubscribes from wallet updates.
    /// </summary>
    public async Task Unsubscribe(string address)
    {
        var normalizedAddress = address.Trim().ToLowerInvariant();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalizedAddress);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from {Address}", Context.ConnectionId, normalizedAddress);
        await Clients.Caller.SendAsync("Unsubscribed", new { address = normalizedAddress });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
