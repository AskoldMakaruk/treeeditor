using Microsoft.AspNetCore.SignalR;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Api.Realtime;

/// <summary>Broadcasts change notifications over SignalR.</summary>
public sealed class SignalRTreeChangeNotifier : ITreeChangeNotifier
{
    private readonly IHubContext<TreeHub> _hub;
    private readonly ILogger<SignalRTreeChangeNotifier> _logger;

    public SignalRTreeChangeNotifier(IHubContext<TreeHub> hub, ILogger<SignalRTreeChangeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyChangedAsync(long revision, IReadOnlyList<int> changedIds, CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients.All.SendAsync(
                "TreeChanged",
                new TreeChangedNotification(revision, changedIds),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Real-time delivery is best-effort; clients also reconcile on reconnect.
            _logger.LogWarning(ex, "Failed to broadcast TreeChanged (revision {Revision}, {Count} ids)", revision, changedIds.Count);
        }
    }
}
