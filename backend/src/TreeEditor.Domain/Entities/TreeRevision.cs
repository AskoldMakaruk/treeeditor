namespace TreeEditor.Domain.Entities;

/// <summary>
/// Single-row table holding the monotonically increasing tree revision. Every Apply/Reset
/// bumps it, and touched rows are stamped with the new value, so clients can ask "what
/// changed since revision N" (or compare per-node versions) instead of re-reading the tree.
/// </summary>
public class TreeRevision
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public long Revision { get; set; }
}
