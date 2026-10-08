using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Application.Tests;

/// <summary>
/// In-memory stand-in for <see cref="IElementRepository"/> that mirrors the real set-based
/// semantics (including order-independent resolution of negative parent ids).
/// </summary>
internal sealed class FakeElementRepository : IElementRepository
{
    private readonly Dictionary<int, Element> _elements = new();
    private readonly HashSet<int> _deleted = new();
    private int _nextId = 100;
    private long _revision;

    public void Seed(int id, string value, int? parentId = null)
    {
        _elements[id] = new Element { Id = id, Value = value, ParentId = parentId, Version = 1 };
        _nextId = Math.Max(_nextId, id + 1);
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) => Task.FromResult(_revision);

    public Task<long> BumpRevisionAsync(CancellationToken cancellationToken) => Task.FromResult(++_revision);

    public Task<IReadOnlyList<NodeVersionDto>> GetVersionsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeVersionDto> result = ids
            .Where(id => _elements.ContainsKey(id) && !_deleted.Contains(id))
            .Select(id => new NodeVersionDto(id, _elements[id].Version))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ElementNodeDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        if (!_elements.TryGetValue(id, out var element) || _deleted.Contains(id))
        {
            return Task.FromResult<ElementNodeDto?>(null);
        }

        var hasChildren = _elements.Values.Any(child => child.ParentId == id && !_deleted.Contains(child.Id));
        return Task.FromResult<ElementNodeDto?>(
            new ElementNodeDto(id, element.Value, element.ParentId, hasChildren, element.Version, element.UpdatedAt));
    }

    public Task<int> UpdateValuesAsync(
        IReadOnlyList<UpdateOperation> updates,
        long version,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var update in updates)
        {
            if (_elements.TryGetValue(update.Id, out var element) && !_deleted.Contains(update.Id))
            {
                element.Value = update.Value;
                element.Version = version;
                affected++;
            }
        }

        return Task.FromResult(affected);
    }

    public Task<IReadOnlyList<AddedElementResult>> AddRangeAsync(
        IReadOnlyList<AddOperation> additions,
        long version,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, int>();
        var pending = additions.ToList();
        var results = new List<AddedElementResult>();

        while (pending.Count > 0)
        {
            var progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var addition = pending[i];
                int parentId;
                if (addition.ParentId >= 0)
                {
                    parentId = addition.ParentId;
                }
                else if (map.TryGetValue(addition.ParentId, out var real))
                {
                    parentId = real;
                }
                else
                {
                    continue;
                }

                var id = _nextId++;
                _elements[id] = new Element { Id = id, Value = addition.Value, ParentId = parentId, Version = version };
                map[addition.TempId] = id;
                results.Add(new AddedElementResult(addition.TempId, id));
                pending.RemoveAt(i);
                progressed = true;
            }

            if (!progressed)
            {
                throw new InvalidOperationException("A negative parent id could not be resolved.");
            }
        }

        return Task.FromResult<IReadOnlyList<AddedElementResult>>(results);
    }

    public Task<int> SoftDeleteSubtreeAsync(int id, long version, CancellationToken cancellationToken)
    {
        if (!_elements.ContainsKey(id) || _deleted.Contains(id))
        {
            return Task.FromResult(0);
        }

        var stack = new Stack<int>();
        stack.Push(id);
        var count = 0;
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (_deleted.Contains(current))
            {
                continue;
            }

            _deleted.Add(current);
            _elements[current].Version = version;
            count++;
            foreach (var child in _elements.Values.Where(e => e.ParentId == current).Select(e => e.Id))
            {
                stack.Push(child);
            }
        }

        return Task.FromResult(count);
    }

    public Task TouchManyAsync(IReadOnlyList<int> ids, long version, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            if (_elements.TryGetValue(id, out var element) && !_deleted.Contains(id))
            {
                element.Version = version;
            }
        }

        return Task.CompletedTask;
    }

    public Task BulkInsertAsync(IEnumerable<Element> elements, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ClearAllAsync(CancellationToken cancellationToken)
    {
        _elements.Clear();
        _deleted.Clear();
        return Task.CompletedTask;
    }

    public Task SyncIdentitySequenceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ElementNodeDto>>([]);

    public Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ElementNodeDto>>([]);

    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => Task.FromResult<ITransaction>(new FakeTransaction());

    private sealed class FakeTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class RecordingLock : IDistributedLock
{
    public List<string> AcquiredKeys { get; } = [];

    public Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AcquiredKeys.Add(key);
        return Task.FromResult<IAsyncDisposable>(new Handle());
    }

    private sealed class Handle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class RecordingNotifier : ITreeChangeNotifier
{
    public sealed record Call(long Revision, IReadOnlyList<int> ChangedIds, bool Reset);

    public List<Call> Calls { get; } = [];

    public Task NotifyChangedAsync(
        long revision,
        IReadOnlyList<int> changedIds,
        bool reset,
        CancellationToken cancellationToken)
    {
        Calls.Add(new Call(revision, changedIds, reset));
        return Task.CompletedTask;
    }
}

internal sealed class NoopCache : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeSampleData : ISampleDataProvider
{
    public IReadOnlyList<Element> GetSampleData() =>
        [new Element { Id = 1, Value = "root", Version = 1 }];
}
