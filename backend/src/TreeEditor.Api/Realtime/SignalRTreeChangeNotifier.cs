using Microsoft.AspNetCore.SignalR;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Api.Realtime;

/// <summary>
/// Broadcasts change notifications over SignalR, scoped per connection to the nodes that client
/// currently has visible. New roots are always delivered (the top level is always visible), and a
/// Reset is sent to everyone because it replaces the whole tree.
/// </summary>
public sealed class SignalRTreeChangeNotifier(
    IHubContext<TreeHub> hub,
    TreeVisibilityTracker visibility,
    ILogger<SignalRTreeChangeNotifier> logger) : ITreeChangeNotifier
{
    public async Task NotifyChangedAsync(
        long revision,
        IReadOnlyList<int> changedIds,
        IReadOnlyList<int> newRootIds,
        bool reset,
        CancellationToken cancellationToken)
    {
        try
        {
            if (reset)
            {
                await hub.Clients.All.SendAsync(
                    "TreeChanged",
                    new TreeChangedNotification(revision, [], true),
                    cancellationToken);
                return;
            }

            if (changedIds.Count == 0)
            {
                return;
            }

            var newRoots = newRootIds.ToHashSet();

            foreach (var connectionId in visibility.ConnectionIds)
            {
                // A connection that has not reported its visible nodes yet receives everything, so
                // it can still converge (and it will register visibility on its next render).
                var relevant = visibility.TryGet(connectionId, out var visible)
                    ? changedIds.Where(id => newRoots.Contains(id) || visible.Contains(id)).ToList()
                    : changedIds.ToList();

                if (relevant.Count == 0)
                {
                    continue;
                }

                await hub.Clients.Client(connectionId).SendAsync(
                    "TreeChanged",
                    new TreeChangedNotification(revision, relevant, false),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Real-time delivery is best-effort; clients also reconcile on reconnect.
            logger.LogWarning(
                ex,
                "Failed to broadcast TreeChanged (revision {Revision}, {Count} ids, reset {Reset})",
                revision,
                changedIds.Count,
                reset);
        }
    }
}
