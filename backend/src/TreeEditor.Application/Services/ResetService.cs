using TreeEditor.Application.Caching;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Application.Services;

/// <summary>Restores the database to the initial sample data and flushes cached reads.</summary>
public sealed class ResetService(
    IElementRepository repository,
    ICacheService cache,
    IDistributedLock distributedLock,
    ISampleDataProvider sampleData,
    ITreeChangeNotifier notifier)
    : IResetService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    public async Task<long> ResetAsync(CancellationToken cancellationToken)
    {
        await using var handle = await distributedLock.AcquireAsync(CacheKeys.TreeLock, LockTimeout, cancellationToken);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var revision = await repository.BumpRevisionAsync(cancellationToken);

        await repository.ClearAllAsync(cancellationToken);

        var sample = sampleData.GetSampleData();
        foreach (var element in sample)
        {
            element.Version = revision;
        }

        await repository.BulkInsertAsync(sample, cancellationToken);
        await repository.SyncIdentitySequenceAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await cache.RemoveByPrefixAsync(CacheKeys.TreePrefix, cancellationToken);
        await notifier.NotifyChangedAsync(revision, [], [], reset: true, cancellationToken);

        return revision;
    }
}
