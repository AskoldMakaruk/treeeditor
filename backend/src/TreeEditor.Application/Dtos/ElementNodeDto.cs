namespace TreeEditor.Application.Dtos;

/// <summary>
/// A node projected for the UI. <see cref="HasChildren"/> lets the tree render an expand arrow;
/// <see cref="Version"/> is the tree revision the row last changed at, which clients compare to
/// detect changes made elsewhere (no hashing needed). <see cref="UpdatedAt"/> is when the row was
/// last changed on the server, shown when resolving a conflict against a local edit.
/// </summary>
public sealed record ElementNodeDto(
    int Id,
    string Value,
    int? ParentId,
    bool HasChildren,
    long Version,
    DateTimeOffset UpdatedAt);
