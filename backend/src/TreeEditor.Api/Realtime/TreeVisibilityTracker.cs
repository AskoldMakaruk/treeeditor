using System.Collections.Concurrent;

namespace TreeEditor.Api.Realtime;

/// <summary>
/// Tracks, per SignalR connection, the element ids the client currently has visible in its UI
/// session. Change notifications are filtered against this so a client is only told about nodes it
/// can actually see (and, via a parent's version bump, the children of visible nodes).
/// </summary>
public sealed class TreeVisibilityTracker
{
    private readonly ConcurrentDictionary<string, HashSet<int>> _visible = new();

    public void Set(string connectionId, IEnumerable<int> ids) =>
        _visible[connectionId] = new HashSet<int>(ids);

    public bool TryGet(string connectionId, out HashSet<int> ids) =>
        _visible.TryGetValue(connectionId, out ids!);

    public void Remove(string connectionId) => _visible.TryRemove(connectionId, out _);

    public IReadOnlyCollection<string> ConnectionIds => _visible.Keys.ToArray();
}
