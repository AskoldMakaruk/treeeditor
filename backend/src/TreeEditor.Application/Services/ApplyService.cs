using TreeEditor.Application.Caching;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Application.Services;

/// <summary>
/// Applies the browser cache's pending edits, additions and deletions in a single transaction
/// while holding the apply lock. Deletion cascades to unloaded descendants in the database.
/// Touched rows are stamped with a new tree revision, and connected clients are notified.
/// </summary>
public sealed class ApplyService : IApplyService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly IElementRepository _repository;
    private readonly ICacheService _cache;
    private readonly IDistributedLock _lock;
    private readonly ITreeChangeNotifier _notifier;
    private readonly TimeProvider _clock;

    public ApplyService(
        IElementRepository repository,
        ICacheService cache,
        IDistributedLock distributedLock,
        ITreeChangeNotifier notifier,
        TimeProvider clock)
    {
        _repository = repository;
        _cache = cache;
        _lock = distributedLock;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<ApplyResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken)
    {
        await using var handle = await _lock.AcquireAsync(CacheKeys.ApplyLock, LockTimeout, cancellationToken);
        await using var transaction = await _repository.BeginTransactionAsync(cancellationToken);

        var revision = await _repository.BumpRevisionAsync(cancellationToken);
        var changedIds = new HashSet<int>();

        var updated = 0;
        var added = 0;
        var deleted = 0;
        var additions = new List<AddedElementResult>();
        var tempToReal = new Dictionary<int, int>();

        foreach (var update in request.Updates)
        {
            var value = RequireValue(update.Value, update.Id);
            var valueChanged = await _repository.UpdateValueAsync(update.Id, value, revision, cancellationToken);
            if (!valueChanged)
            {
                throw new DomainException($"Element {update.Id} does not exist or has been deleted.");
            }

            changedIds.Add(update.Id);
            updated++;
        }

        foreach (var addition in request.Additions)
        {
            var value = RequireValue(addition.Value, addition.TempId);

            var parentId = addition.ParentId;
            if (parentId < 0)
            {
                // The parent is another pending addition; resolve it to the id assigned above.
                if (!tempToReal.TryGetValue(parentId, out var realParentId))
                {
                    throw new DomainException(
                        $"Parent {parentId} of new element {addition.TempId} was not added in the same batch.");
                }

                parentId = realParentId;
            }
            else
            {
                var parent = await _repository.GetByIdAsync(parentId, cancellationToken);
                if (parent is null)
                {
                    throw new DomainException($"Parent element {parentId} does not exist or has been deleted.");
                }
            }

            var now = _clock.GetUtcNow();
            var id = await _repository.AddAsync(
                new Element
                {
                    Value = value,
                    ParentId = parentId,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                revision,
                cancellationToken);

            tempToReal[addition.TempId] = id;
            additions.Add(new AddedElementResult(addition.TempId, id));
            changedIds.Add(id);
            changedIds.Add(parentId);
            added++;
        }

        foreach (var id in request.Deletions)
        {
            var node = await _repository.GetByIdAsync(id, cancellationToken);
            var affected = await _repository.SoftDeleteSubtreeAsync(id, revision, cancellationToken);
            if (affected == 0)
            {
                throw new DomainException($"Element {id} does not exist or has already been deleted.");
            }

            changedIds.Add(id);
            if (node?.ParentId is int parentId)
            {
                changedIds.Add(parentId);
            }

            deleted += affected;
        }

        await transaction.CommitAsync(cancellationToken);

        // Any node may have changed (or a subtree may have been removed): drop all tree reads.
        await _cache.RemoveByPrefixAsync(CacheKeys.TreePrefix, cancellationToken);

        var changed = changedIds.ToList();
        await _notifier.NotifyChangedAsync(revision, changed, cancellationToken);

        return new ApplyResult(updated, added, deleted, additions, revision);
    }

    private static string RequireValue(string value, int id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"Value for element {id} cannot be empty.");
        }

        return value.Trim();
    }
}
