using Microsoft.AspNetCore.SignalR;

namespace TreeEditor.Api.Realtime;

/// <summary>
/// Real-time channel used to tell connected clients that the tree changed and which ids were
/// touched, so they can run a version check and refresh only what they hold. Clients also report
/// which nodes are visible in their UI session so notifications can be scoped to them.
/// </summary>
public sealed class TreeHub(TreeVisibilityTracker visibility) : Hub
{
    /// <summary>Replaces this connection's set of visible element ids.</summary>
    public Task SetVisible(int[] ids)
    {
        visibility.Set(Context.ConnectionId, ids);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        visibility.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
