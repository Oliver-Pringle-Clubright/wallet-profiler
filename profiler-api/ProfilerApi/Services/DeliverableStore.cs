using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ProfilerApi.Services;

public class DeliverableStore
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public DeliverableStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> StoreAsync(object payload, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(
            Key(id),
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
        return id;
    }

    public async Task<string?> FetchAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id)) return null;
        return await _cache.GetStringAsync(Key(id), ct);
    }

    private static string Key(string id) => $"deliverable:{id}";

    private static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id)
        && id.Length == 32
        && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}
