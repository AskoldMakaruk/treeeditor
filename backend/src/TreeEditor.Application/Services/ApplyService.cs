using TreeEditor.Application.Caching;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain;

namespace TreeEditor.Application.Services;

/// <summary>
/// Applies the browser cache's pending edits, additions and deletions in a single transaction
/// while holding the apply lock. Deletion cascades to unloaded descendants in the database.
/// Touched rows are stamped with a new tree revision, and connected clients are notified.
/// Batch operations are set-based so large batches stay within a few round trips.
/// </summary>
public sealed class ApplyService(
    IElementRepository repository,
    ICacheService cache,
    IDistributedLock distributedLock,
    ITreeChangeNotifier notifier)
    : IApplyService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    public async Task<ApplyResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken)
    {
        await using var handle = await distributedLock.AcquireAsync(CacheKeys.TreeLock, LockTimeout, cancellationToken);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var revision = await repository.BumpRevisionAsync(cancellationToken);
        var changedIds = new HashSet<int>();
        var touchedParents = new HashSet<int>();
        var newRootIds = new HashSet<int>();

        var updated = await ApplyUpdatesAsync(request.Updates, revision, changedIds, cancellationToken);
        var (added, additions) = await ApplyAdditionsAsync(
            request.Additions, revision, changedIds, touchedParents, newRootIds, cancellationToken);
        var deleted = await ApplyDeletionsAsync(
            request.Deletions, revision, changedIds, touchedParents, cancellationToken);

        // Adding/removing a child changes the parent's children-set -> bump its version. The parent
        // is part of the delta too, so a client that only has the parent visible is told its
        // children-set changed (it updates the expand arrow and only loads children if expanded).
        if (touchedParents.Count > 0)
        {
            await repository.TouchManyAsync(touchedParents.ToList(), revision, cancellationToken);
            changedIds.UnionWith(touchedParents);
        }

        await transaction.CommitAsync(cancellationToken);

        // Any node may have changed (or a subtree may have been removed): drop all tree reads.
        await cache.RemoveByPrefixAsync(CacheKeys.TreePrefix, cancellationToken);

        var changed = changedIds.ToList();
        await notifier.NotifyChangedAsync(revision, changed, newRootIds.ToList(), reset: false, cancellationToken);

        return new ApplyResult(updated, added, deleted, additions, revision);
    }

    private async Task<int> ApplyUpdatesAsync(
        IReadOnlyList<UpdateOperation> updates,
        long revision,
        HashSet<int> changedIds,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        var ids = updates.Select(update => update.Id).Distinct().ToArray();
        var existing = (await repository.GetVersionsAsync(ids, cancellationToken))
            .Select(node => node.Id)
            .ToHashSet();

        if (FirstMissing(ids, existing) is int missingId)
        {
            throw new DomainException($"Element {missingId} does not exist or has been deleted.");
        }

        var normalized = updates
            .Select(update => new UpdateOperation(update.Id, RequireValue(update.Value, update.Id)))
            .ToList();

        var affected = await repository.UpdateValuesAsync(normalized, revision, cancellationToken);

        foreach (var update in updates)
        {
            changedIds.Add(update.Id);
        }

        return affected;
    }

    private async Task<(int Added, IReadOnlyList<AddedElementResult> Result)> ApplyAdditionsAsync(
        IReadOnlyList<AddOperation> additions,
        long revision,
        HashSet<int> changedIds,
        HashSet<int> touchedParents,
        HashSet<int> newRootIds,
        CancellationToken cancellationToken)
    {
        if (additions.Count == 0)
        {
            return (0, []);
        }

        var tempIds = additions.Select(addition => addition.TempId).ToHashSet();

        // Existing (non-negative) parents must exist; negative ids must refer to another addition;
        // a null parent creates a root element and needs no validation.
        var realParentIds = additions
            .Select(addition => addition.ParentId)
            .Where(parentId => parentId is >= 0)
            .Select(parentId => parentId!.Value)
            .Distinct()
            .ToArray();

        if (realParentIds.Length > 0)
        {
            var existing = (await repository.GetVersionsAsync(realParentIds, cancellationToken))
                .Select(node => node.Id)
                .ToHashSet();

            if (FirstMissing(realParentIds, existing) is int missingParent)
            {
                throw new DomainException($"Parent element {missingParent} does not exist or has been deleted.");
            }
        }

        foreach (var addition in additions)
        {
            if (addition.ParentId is int parentId && parentId < 0 && !tempIds.Contains(parentId))
            {
                throw new DomainException(
                    $"Parent {parentId} of new element {addition.TempId} was not added in the same batch.");
            }
        }

        var normalized = additions
            .Select(addition => new AddOperation(
                addition.TempId,
                addition.ParentId,
                RequireValue(addition.Value, addition.TempId)))
            .ToList();

        var result = await repository.AddRangeAsync(normalized, revision, cancellationToken);
        var tempToReal = result.ToDictionary(item => item.TempId, item => item.Id);

        foreach (var addition in additions)
        {
            var newId = tempToReal[addition.TempId];
            changedIds.Add(newId);

            if (addition.ParentId is null)
            {
                newRootIds.Add(newId);
            }
            else if (addition.ParentId is int parentId)
            {
                touchedParents.Add(parentId >= 0 ? parentId : tempToReal[parentId]);
            }
        }

        return (additions.Count, result);
    }

    private async Task<int> ApplyDeletionsAsync(
        IReadOnlyList<int> deletions,
        long revision,
        HashSet<int> changedIds,
        HashSet<int> touchedParents,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var id in deletions)
        {
            var node = await repository.GetByIdAsync(id, cancellationToken);
            var affected = await repository.SoftDeleteSubtreeAsync(id, revision, cancellationToken);
            if (affected == 0)
            {
                throw new DomainException($"Element {id} does not exist or has already been deleted.");
            }

            changedIds.Add(id);
            if (node?.ParentId is int parentId)
            {
                touchedParents.Add(parentId);
            }

            deleted += affected;
        }

        return deleted;
    }

    private static int? FirstMissing(IReadOnlyList<int> ids, HashSet<int> existing)
    {
        foreach (var id in ids)
        {
            if (!existing.Contains(id))
            {
                return id;
            }
        }

        return null;
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
