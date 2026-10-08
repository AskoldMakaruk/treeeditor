using TreeEditor.Application.Caching;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Services;
using TreeEditor.Domain;

namespace TreeEditor.Application.Tests;

public sealed class ApplyServiceTests
{
    private static readonly UpdateOperation[] NoUpdates = [];
    private static readonly AddOperation[] NoAdditions = [];
    private static readonly int[] NoDeletions = [];

    private static (ApplyService Service, FakeElementRepository Repository, RecordingLock Lock, RecordingNotifier Notifier) Build()
    {
        var repository = new FakeElementRepository();
        var distributedLock = new RecordingLock();
        var notifier = new RecordingNotifier();
        var service = new ApplyService(repository, new NoopCache(), distributedLock, notifier);
        return (service, repository, distributedLock, notifier);
    }

    [Fact]
    public async Task Apply_uses_the_shared_tree_lock()
    {
        var (service, repository, distributedLock, _) = Build();
        repository.Seed(1, "root");

        await service.ApplyAsync(new ApplyRequest([new UpdateOperation(1, "renamed")], NoAdditions, NoDeletions), default);

        Assert.Equal([CacheKeys.TreeLock], distributedLock.AcquiredKeys);
    }

    [Fact]
    public async Task Apply_updates_values_in_a_batch()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");
        repository.Seed(2, "a", 1);
        repository.Seed(3, "b", 1);

        var result = await service.ApplyAsync(
            new ApplyRequest([new UpdateOperation(2, "a2"), new UpdateOperation(3, "b2")], NoAdditions, NoDeletions),
            default);

        Assert.Equal(2, result.UpdatedCount);
        Assert.Equal("a2", (await repository.GetByIdAsync(2, default))!.Value);
        Assert.Equal("b2", (await repository.GetByIdAsync(3, default))!.Value);
    }

    [Fact]
    public async Task Apply_throws_when_updating_a_missing_element()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");

        await Assert.ThrowsAsync<DomainException>(() => service.ApplyAsync(
            new ApplyRequest([new UpdateOperation(999, "nope")], NoAdditions, NoDeletions),
            default));
    }

    [Fact]
    public async Task Apply_resolves_additions_regardless_of_order()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");

        // The child (-2) is listed before its pending parent (-1).
        var result = await service.ApplyAsync(
            new ApplyRequest(
                NoUpdates,
                [new AddOperation(-2, -1, "child"), new AddOperation(-1, 1, "parent")],
                NoDeletions),
            default);

        Assert.Equal(2, result.AddedCount);
        var parent = result.Added.Single(item => item.TempId == -1);
        var child = result.Added.Single(item => item.TempId == -2);
        Assert.Equal(parent.Id, (await repository.GetByIdAsync(child.Id, default))!.ParentId);
    }

    [Fact]
    public async Task Apply_throws_when_a_negative_parent_is_not_in_the_batch()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");

        await Assert.ThrowsAsync<DomainException>(() => service.ApplyAsync(
            new ApplyRequest(NoUpdates, [new AddOperation(-2, -3, "orphan")], NoDeletions),
            default));
    }

    [Fact]
    public async Task Apply_batches_many_updates_and_additions()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");
        var updates = new List<UpdateOperation>();
        for (var id = 2; id <= 200; id++)
        {
            repository.Seed(id, $"value-{id}", 1);
            updates.Add(new UpdateOperation(id, $"updated-{id}"));
        }

        var additions = Enumerable.Range(0, 200)
            .Select(index => new AddOperation(-(index + 1), 1, $"new-{index}"))
            .ToList();

        var result = await service.ApplyAsync(new ApplyRequest(updates, additions, NoDeletions), default);

        Assert.Equal(199, result.UpdatedCount);
        Assert.Equal(200, result.AddedCount);
        Assert.Equal(200, result.Added.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Apply_cascades_deletion_to_unloaded_descendants()
    {
        var (service, repository, _, _) = Build();
        repository.Seed(1, "root");
        repository.Seed(2, "branch", 1);
        repository.Seed(3, "leaf", 2);

        var result = await service.ApplyAsync(
            new ApplyRequest(NoUpdates, NoAdditions, [2]),
            default);

        Assert.Equal(2, result.DeletedCount);
        Assert.Null(await repository.GetByIdAsync(2, default));
        Assert.Null(await repository.GetByIdAsync(3, default));
    }

    [Fact]
    public async Task Apply_notifies_without_the_reset_flag()
    {
        var (service, repository, _, notifier) = Build();
        repository.Seed(1, "root");

        await service.ApplyAsync(new ApplyRequest([new UpdateOperation(1, "renamed")], NoAdditions, NoDeletions), default);

        var call = Assert.Single(notifier.Calls);
        Assert.False(call.Reset);
        Assert.Contains(1, call.ChangedIds);
    }
}
