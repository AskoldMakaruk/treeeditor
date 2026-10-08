using Microsoft.AspNetCore.SignalR;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Api.Realtime;

/// <summary>Broadcasts change notifications over SignalR.</summary>
public sealed class SignalRTreeChangeNotifier(IHubContext<TreeHub> hub, ILogger<SignalRTreeChangeNotifier> logger) : ITreeChangeNotifier
{
    public async Task NotifyChangedAsync(
        long revision,
        IReadOnlyList<int> changedIds,
        bool reset,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(
                "TreeChanged",
                new TreeChangedNotification(revision, changedIds, reset),
                cancellationToken);
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
