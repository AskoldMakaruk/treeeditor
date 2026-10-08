using TreeEditor.Application.Dtos;

namespace TreeEditor.Application.Interfaces;

/// <summary>Version-based synchronisation for partially loaded clients.</summary>
public interface ISyncService
{
    Task<long> GetRevisionAsync(CancellationToken cancellationToken);

    Task<TreeCheckResult> CheckAsync(TreeCheckRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Notifies connected clients that the tree changed. Implemented with SignalR in the API layer
/// (with a Redis backplane so it works across instances); a no-op is used elsewhere.
/// </summary>
public interface ITreeChangeNotifier
{
    Task NotifyChangedAsync(
        long revision,
        IReadOnlyList<int> changedIds,
        bool reset,
        CancellationToken cancellationToken);
}

/// <summary>No-op implementation for hosts without real-time transport (e.g. tests).</summary>
public sealed class NullTreeChangeNotifier : ITreeChangeNotifier
{
    public Task NotifyChangedAsync(
        long revision,
        IReadOnlyList<int> changedIds,
        bool reset,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
