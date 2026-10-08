using TreeEditor.Application.Caching;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Application.Services;

/// <summary>Restores the database to the initial sample data and flushes cached reads.</summary>
public sealed class ResetService : IResetService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly IElementRepository _repository;
    private readonly ICacheService _cache;
    private readonly IDistributedLock _lock;
    private readonly ISampleDataProvider _sampleData;
    private readonly ITreeChangeNotifier _notifier;

    public ResetService(
        IElementRepository repository,
        ICacheService cache,
        IDistributedLock distributedLock,
        ISampleDataProvider sampleData,
        ITreeChangeNotifier notifier)
    {
        _repository = repository;
        _cache = cache;
        _lock = distributedLock;
        _sampleData = sampleData;
        _notifier = notifier;
    }

    public async Task<long> ResetAsync(CancellationToken cancellationToken)
    {
        await using var handle = await _lock.AcquireAsync(CacheKeys.ResetLock, LockTimeout, cancellationToken);
        await using var transaction = await _repository.BeginTransactionAsync(cancellationToken);

        var revision = await _repository.BumpRevisionAsync(cancellationToken);

        await _repository.ClearAllAsync(cancellationToken);

        var sample = _sampleData.GetSampleData();
        foreach (var element in sample)
        {
            element.Version = revision;
        }

        await _repository.AddRangeAsync(sample, cancellationToken);
        await _repository.SyncIdentitySequenceAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await _cache.RemoveByPrefixAsync(CacheKeys.TreePrefix, cancellationToken);
        await _notifier.NotifyChangedAsync(revision, [], cancellationToken);

        return revision;
    }
}
