using System.Collections.Concurrent;

namespace ProfilerApi.Services;

/// <summary>
/// Simple API key authentication and rate limiting service.
/// Keys are stored in appsettings under "ApiKeys" section.
/// Each key has a name, tier, and rate limit.
/// </summary>
public class ApiKeyAuthService : IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyAuthService> _logger;
    private readonly ConcurrentDictionary<string, ApiKeyInfo> _keys = new();
    private readonly ConcurrentDictionary<string, RateLimitState> _rateLimits = new();
    private readonly Timer _cleanupTimer;

    public ApiKeyAuthService(IConfiguration config, ILogger<ApiKeyAuthService> logger)
    {
        _config = config;
        _logger = logger;
        LoadKeys();

        // Cleanup stale rate limit entries every 5 minutes
        _cleanupTimer = new Timer(_ => CleanupStaleEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void CleanupStaleEntries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var staleKeys = _rateLimits
            .Where(kv => { lock (kv.Value) { return kv.Value.Requests.Count == 0 || kv.Value.Requests.Peek() < cutoff; } })
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in staleKeys)
            _rateLimits.TryRemove(key, out _);

        if (staleKeys.Count > 0)
            _logger.LogDebug("Cleaned up {Count} stale rate limit entries", staleKeys.Count);
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private void LoadKeys()
    {
        var keysSection = _config.GetSection("ApiKeys");
        if (!keysSection.Exists()) return;

        foreach (var child in keysSection.GetChildren())
        {
            var key = child["Key"];
            if (string.IsNullOrEmpty(key)) continue;

            _keys[key] = new ApiKeyInfo
            {
                Name = child["Name"] ?? "unnamed",
                Tier = child["Tier"] ?? "standard",
                RateLimit = int.TryParse(child["RateLimit"], out var rl) ? rl : 60,
                WindowSeconds = int.TryParse(child["WindowSeconds"], out var ws) ? ws : 60
            };
        }

        _logger.LogInformation("Loaded {Count} API keys", _keys.Count);
    }

    /// <summary>
    /// Returns true if auth is enabled (API keys configured).
    /// When no keys are configured, all requests pass through (dev mode).
    /// </summary>
    public bool IsEnabled => _keys.Count > 0;

    /// <summary>
    /// Validates an API key and checks rate limits.
    /// Returns (isValid, errorMessage, keyInfo).
    /// </summary>
    public (bool IsValid, string? Error, ApiKeyInfo? Info) ValidateAndCheckRate(string? apiKey)
    {
        if (!IsEnabled)
            return (true, null, null);

        if (string.IsNullOrEmpty(apiKey))
            return (false, "API key required. Pass via X-API-Key header.", null);

        if (!_keys.TryGetValue(apiKey, out var info))
            return (false, "Invalid API key.", null);

        // Rate limiting using sliding window
        var state = _rateLimits.GetOrAdd(apiKey, _ => new RateLimitState());
        lock (state)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-info.WindowSeconds);

            // Remove expired entries
            while (state.Requests.Count > 0 && state.Requests.Peek() < windowStart)
                state.Requests.Dequeue();

            if (state.Requests.Count >= info.RateLimit)
            {
                var retryAfter = (state.Requests.Peek() - windowStart).TotalSeconds;
                return (false, $"Rate limit exceeded. {info.RateLimit} requests per {info.WindowSeconds}s. Retry after {retryAfter:F0}s.", info);
            }

            state.Requests.Enqueue(now);
        }

        return (true, null, info);
    }

    public class ApiKeyInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Tier { get; set; } = "standard";
        public int RateLimit { get; set; } = 60;
        public int WindowSeconds { get; set; } = 60;
    }

    private class RateLimitState
    {
        public Queue<DateTime> Requests { get; } = new();
    }
}
