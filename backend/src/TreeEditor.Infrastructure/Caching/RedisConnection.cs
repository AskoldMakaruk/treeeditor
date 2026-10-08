using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace TreeEditor.Infrastructure.Caching;

/// <summary>
/// Owns a single lazily-created Redis connection. All access is defensive: if Redis is
/// unreachable the returned database is null and callers degrade gracefully.
/// </summary>
public sealed class RedisConnection : IDisposable
{
    private readonly Lazy<IConnectionMultiplexer?> _connection;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConnection> _logger;

    public RedisConnection(IOptions<RedisOptions> options, ILogger<RedisConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connection = new Lazy<IConnectionMultiplexer?>(Create, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IConnectionMultiplexer? Multiplexer => _connection.Value;

    public IDatabase? TryGetDatabase()
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            return Multiplexer?.GetDatabase();
        }
        catch
        {
            return null;
        }
    }

    private IConnectionMultiplexer? Create()
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Redis is disabled; running without a cache and using in-process locks.");
            return null;
        }

        try
        {
            var configuration = ConfigurationOptions.Parse(_options.Configuration);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 2000;
            return ConnectionMultiplexer.Connect(configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to Redis at {Configuration}; continuing without a cache.", _options.Configuration);
            return null;
        }
    }

    public void Dispose() => (_connection.IsValueCreated ? _connection.Value : null)?.Dispose();
}
