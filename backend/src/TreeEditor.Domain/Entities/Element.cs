namespace TreeEditor.Domain.Entities;

/// <summary>
/// A single node of the tree. Parent/child links are immutable once created:
/// an element may be created with a parent and soft-deleted, but never re-parented.
/// </summary>
public class Element
{
    public int Id { get; set; }

    public string Value { get; set; } = string.Empty;

    public int? ParentId { get; set; }

    public Element? Parent { get; set; }

    public ICollection<Element> Children { get; set; } = new List<Element>();

    public bool IsDeleted { get; set; }

    /// <summary>
    /// The global tree revision this row was last changed at. Used to detect changes
    /// for caching/synchronisation without loading the whole tree.
    /// </summary>
    public long Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
