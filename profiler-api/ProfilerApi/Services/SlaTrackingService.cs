using System.Collections.Concurrent;
using System.Diagnostics;

namespace ProfilerApi.Services;

/// <summary>
/// Tracks response time SLA metrics for all endpoints.
/// Provides p50, p95, p99 latency percentiles and SLA compliance tracking.
/// </summary>
public class SlaTrackingService
{
    private readonly ConcurrentDictionary<string, EndpointMetrics> _metrics = new();
    private readonly ILogger<SlaTrackingService> _logger;

    // SLA targets in milliseconds
    public static readonly Dictionary<string, int> SlaTargetsMs = new()
    {
        ["profile_basic"] = 5000,
        ["profile_standard"] = 15000,
        ["profile_premium"] = 15000,
        ["profile_batch"] = 30000,
        ["profile_multi_chain"] = 30000,
        ["trust"] = 2000,
        ["monitor"] = 500,
    };

    public SlaTrackingService(ILogger<SlaTrackingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records a request completion for an endpoint.
    /// </summary>
    public void Record(string endpoint, long elapsedMs, bool success)
    {
        var metrics = _metrics.GetOrAdd(endpoint, _ => new EndpointMetrics());
        lock (metrics)
        {
            metrics.TotalRequests++;
            if (success) metrics.SuccessCount++;
            else metrics.ErrorCount++;

            metrics.Latencies.Add(elapsedMs);

            // Keep only last 1000 latencies for percentile calculation
            if (metrics.Latencies.Count > 1000)
                metrics.Latencies.RemoveAt(0);

            // Check SLA
            if (SlaTargetsMs.TryGetValue(endpoint, out var target) && elapsedMs > target)
            {
                metrics.SlaBreaches++;
                _logger.LogWarning("SLA breach on {Endpoint}: {Elapsed}ms > {Target}ms", endpoint, elapsedMs, target);
            }
        }
    }

    /// <summary>
    /// Creates a Stopwatch-based tracker that auto-records on dispose.
    /// </summary>
    public RequestTracker Track(string endpoint)
    {
        return new RequestTracker(this, endpoint);
    }

    /// <summary>
    /// Returns SLA metrics for all endpoints.
    /// </summary>
    public SlaReport GetReport()
    {
        var endpoints = new Dictionary<string, EndpointSlaMetrics>();

        foreach (var (endpoint, metrics) in _metrics)
        {
            lock (metrics)
            {
                if (metrics.TotalRequests == 0) continue;

                var sorted = metrics.Latencies.OrderBy(l => l).ToList();
                var slaTarget = SlaTargetsMs.GetValueOrDefault(endpoint, 10000);

                endpoints[endpoint] = new EndpointSlaMetrics
                {
                    TotalRequests = metrics.TotalRequests,
                    SuccessCount = metrics.SuccessCount,
                    ErrorCount = metrics.ErrorCount,
                    SlaBreaches = metrics.SlaBreaches,
                    SlaCompliancePct = metrics.TotalRequests > 0
                        ? Math.Round((1.0 - (double)metrics.SlaBreaches / metrics.TotalRequests) * 100, 2)
                        : 100,
                    SlaTargetMs = slaTarget,
                    P50Ms = Percentile(sorted, 0.50),
                    P95Ms = Percentile(sorted, 0.95),
                    P99Ms = Percentile(sorted, 0.99),
                    AvgMs = sorted.Count > 0 ? (long)sorted.Average() : 0,
                    MinMs = sorted.Count > 0 ? sorted.First() : 0,
                    MaxMs = sorted.Count > 0 ? sorted.Last() : 0
                };
            }
        }

        return new SlaReport
        {
            GeneratedAt = DateTime.UtcNow,
            Endpoints = endpoints
        };
    }

    private static long Percentile(List<long> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private class EndpointMetrics
    {
        public long TotalRequests { get; set; }
        public long SuccessCount { get; set; }
        public long ErrorCount { get; set; }
        public long SlaBreaches { get; set; }
        public List<long> Latencies { get; } = [];
    }

    /// <summary>
    /// Auto-recording request tracker (disposable pattern).
    /// </summary>
    public class RequestTracker : IDisposable
    {
        private readonly SlaTrackingService _service;
        private readonly string _endpoint;
        private readonly Stopwatch _sw;
        private bool _recorded;
        private bool _success = true;

        public RequestTracker(SlaTrackingService service, string endpoint)
        {
            _service = service;
            _endpoint = endpoint;
            _sw = Stopwatch.StartNew();
        }

        public void MarkFailed() => _success = false;

        public long ElapsedMs => _sw.ElapsedMilliseconds;

        public void Dispose()
        {
            if (_recorded) return;
            _recorded = true;
            _sw.Stop();
            _service.Record(_endpoint, _sw.ElapsedMilliseconds, _success);
        }
    }
}

public class SlaReport
{
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, EndpointSlaMetrics> Endpoints { get; set; } = new();
}

public class EndpointSlaMetrics
{
    public long TotalRequests { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public long SlaBreaches { get; set; }
    public double SlaCompliancePct { get; set; }
    public int SlaTargetMs { get; set; }
    public long P50Ms { get; set; }
    public long P95Ms { get; set; }
    public long P99Ms { get; set; }
    public long AvgMs { get; set; }
    public long MinMs { get; set; }
    public long MaxMs { get; set; }
}
