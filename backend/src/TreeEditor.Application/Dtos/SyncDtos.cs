namespace TreeEditor.Application.Dtos;

/// <summary>Client sends the ids it currently holds; server returns their current versions.</summary>
public sealed record TreeCheckRequest(IReadOnlyList<int> Ids);

public sealed record NodeVersionDto(int Id, long Version);

/// <summary>
/// Result of a version check. <see cref="Nodes"/> holds the current version for each id that still
/// exists; ids present in the request but absent here (listed in <see cref="Deleted"/>) were removed.
/// </summary>
public sealed record TreeCheckResult(long Revision, IReadOnlyList<NodeVersionDto> Nodes, IReadOnlyList<int> Deleted);

public sealed record RevisionDto(long Revision);

/// <summary>Pushed to connected clients when a client applies changes or resets.</summary>
public sealed record TreeChangedNotification(long Revision, IReadOnlyList<int> ChangedIds);
