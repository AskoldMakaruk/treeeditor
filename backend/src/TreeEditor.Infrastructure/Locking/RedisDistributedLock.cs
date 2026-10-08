using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Infrastructure.Locking;

/// <summary>
/// Redis-backed lock with an in-process fallback. The fallback keeps the app correct on a
/// single node when Redis is not running (tests, offline demo).
/// </summary>
public sealed class RedisDistributedLock(Caching.RedisConnection connection, ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new();

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var db = connection.TryGetDatabase();
        var token = Guid.NewGuid().ToString("N");

        if (db is null)
        {
            logger.LogDebug("Redis unavailable; using in-process lock for {Key}", key);
            return await AcquireLocalAsync(key, timeout, cancellationToken);
        }

        var expiry = timeout;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await db.LockTakeAsync(key, token, expiry))
                {
                    return new RedisLockHandle(db, key, token, expiry, logger, cancellationToken);
                }
            }
            catch (RedisException ex)
            {
                logger.LogWarning(ex, "Redis lock failed for {Key}; falling back to the in-process lock", key);
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
        private readonly TimeSpan _expiry;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts;
        private readonly Task _renewal;
        private bool _released;

        public RedisLockHandle(
            IDatabase db,
            RedisKey key,
            RedisValue token,
            TimeSpan expiry,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            _db = db;
            _key = key;
            _token = token;
            _expiry = expiry;
            _logger = logger;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _renewal = RenewAsync();
        }

        /// <summary>Extends the lock periodically so a slow Apply/Reset cannot outlive its TTL.</summary>
        private async Task RenewAsync()
        {
            // Renew at a third of the TTL, but never more often than every 2 seconds.
            var interval = TimeSpan.FromTicks(Math.Max(_expiry.Ticks / 3, TimeSpan.FromSeconds(2).Ticks));
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(_cts.Token))
                {
                    try
                    {
                        if (!await _db.LockExtendAsync(_key, _token, _expiry))
                        {
                            _logger.LogWarning("Lock {Key} could not be extended and may have been lost.", _key);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to extend lock {Key}.", _key);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Handle is being disposed; the lock is released below.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _cts.Cancel();
            try
            {
                await _renewal;
            }
            catch
            {
                // Renewal already observed cancellation.
            }

            try
            {
                await _db.LockReleaseAsync(_key, _token);
            }
            catch
            {
                // Lock expires on its own.
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    private sealed class LocalLockHandle(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
