namespace TreeEditor.Application.Dtos;

public sealed record UpdateOperation(int Id, string Value);

/// <summary>
/// A new element. <see cref="ParentId"/> is a real parent id, a negative temporary id of another
/// addition in the same batch, or <c>null</c> to create a root element.
/// </summary>
public sealed record AddOperation(int TempId, int? ParentId, string Value);

/// <summary>
/// A batch of pending client-cache changes. Applied atomically: either all operations
/// succeed or none of them do.
/// </summary>
public sealed record ApplyRequest(
    IReadOnlyList<UpdateOperation> Updates,
    IReadOnlyList<AddOperation> Additions,
    IReadOnlyList<int> Deletions);

/// <summary>Maps a client-side temporary id to the id the database assigned on Apply.</summary>
public sealed record AddedElementResult(int TempId, int Id);

public sealed record ApplyResult(
    int UpdatedCount,
    int AddedCount,
    int DeletedCount,
    IReadOnlyList<AddedElementResult> Added,
    long Revision);
