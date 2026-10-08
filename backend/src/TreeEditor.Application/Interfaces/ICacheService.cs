namespace TreeEditor.Application.Interfaces;

/// <summary>
/// Server-side cache abstraction (Redis in production, no-op for tests/offline).
/// This is distinct from the browser-side editing cache: it only accelerates reads.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);

    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken);
}
