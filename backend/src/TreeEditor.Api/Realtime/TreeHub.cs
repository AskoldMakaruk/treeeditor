using Microsoft.AspNetCore.SignalR;

namespace TreeEditor.Api.Realtime;

/// <summary>
/// Real-time channel used to tell connected clients that the tree changed and which ids were
/// touched, so they can run a version check and refresh only what they hold.
/// </summary>
public sealed class TreeHub : Hub;
