using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RedisConnection _connection;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(RedisConnection connection, ILogger<RedisCacheService> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var db = _connection.TryGetDatabase();
        if (db is null)
        {
            return default;
        }

        try
        {
            var payload = await db.StringGetAsync(key);
            return payload.IsNullOrEmpty
                ? default
                : JsonSerializer.Deserialize<T>(payload.ToString(), SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache read failed for {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _connection.TryGetDatabase();
        if (db is null)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await db.StringSetAsync(key, payload, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var db = _connection.TryGetDatabase();
        if (db is null)
        {
            return;
        }

        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache eviction failed for {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var multiplexer = _connection.Multiplexer;
        var db = _connection.TryGetDatabase();
        if (multiplexer is null || db is null)
        {
            return;
        }

        try
        {
            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }

                var keys = server.Keys(pattern: $"{prefix}*", pageSize: 500).ToArray();
                if (keys.Length > 0)
                {
                    await db.KeyDeleteAsync(keys);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for prefix {Prefix}", prefix);
        }
    }
}
