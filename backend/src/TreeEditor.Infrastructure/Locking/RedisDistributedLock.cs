using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Infrastructure.Locking;

/// <summary>
/// Redis-backed lock with an in-process fallback. The fallback keeps the app correct on a
/// single node when Redis is not running (tests, offline demo).
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new();

    private readonly Caching.RedisConnection _connection;
    private readonly ILogger<RedisDistributedLock> _logger;

    public RedisDistributedLock(Caching.RedisConnection connection, ILogger<RedisDistributedLock> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var db = _connection.TryGetDatabase();
        var token = Guid.NewGuid().ToString("N");

        if (db is null)
        {
            _logger.LogDebug("Redis unavailable; using in-process lock for {Key}", key);
            return await AcquireLocalAsync(key, timeout, cancellationToken);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await db.LockTakeAsync(key, token, timeout))
                {
                    return new RedisLockHandle(db, key, token);
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis lock failed for {Key}; falling back to the in-process lock", key);
                return await AcquireLocalAsync(key, timeout, cancellationToken);
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new LockUnavailableException(key);
    }

    private static async Task<IAsyncDisposable> AcquireLocalAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var semaphore = LocalLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(timeout, cancellationToken))
        {
            throw new LockUnavailableException(key);
        }

        return new LocalLockHandle(semaphore);
    }

    private sealed class RedisLockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private bool _released;

        public RedisLockHandle(IDatabase db, RedisKey key, RedisValue token)
        {
            _db = db;
            _key = key;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            try
            {
                await _db.LockReleaseAsync(_key, _token);
            }
            catch
            {
                // Lock expires on its own.
            }
        }
    }

    private sealed class LocalLockHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public LocalLockHandle(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
