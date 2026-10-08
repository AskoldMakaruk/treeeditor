namespace TreeEditor.Application.Interfaces;

/// <summary>
/// A cross-process mutex used to serialise <c>Apply</c> and <c>Reset</c>.
/// Falls back to an in-process lock when Redis is unavailable.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Acquires the lock, waiting up to <paramref name="timeout"/>.
    /// Throws <see cref="LockUnavailableException"/> if it cannot be acquired in time.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class LockUnavailableException(string key) : Exception($"Could not acquire lock '{key}' within the timeout.");
