using TreeEditor.Domain.Entities;

namespace TreeEditor.Application.Interfaces;

/// <summary>
/// Persistence port for tree elements. Implemented by EF Core/PostgreSQL in Infrastructure.
/// Read methods return projections that never expose soft-deleted rows.
/// </summary>
public interface IElementRepository
{
    Task<IReadOnlyList<Dtos.ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Dtos.ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken);

    Task<Dtos.ElementNodeDto?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Current global tree revision.</summary>
    Task<long> GetRevisionAsync(CancellationToken cancellationToken);

    /// <summary>Increments and returns the global tree revision (call once per Apply/Reset).</summary>
    Task<long> BumpRevisionAsync(CancellationToken cancellationToken);

    /// <summary>Returns the current version of the requested active ids (missing ids are simply absent).</summary>
    Task<IReadOnlyList<Dtos.NodeVersionDto>> GetVersionsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken);

    /// <summary>Inserts a new element and returns the generated id.</summary>
    Task<int> AddAsync(Element element, long version, CancellationToken cancellationToken);

    /// <summary>Updates the value of a non-deleted element. Returns false when it is missing or deleted.</summary>
    Task<bool> UpdateValueAsync(int id, string value, long version, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes an element and, recursively, every descendant — including descendants that were
    /// never materialised in memory. Returns the number of affected rows.
    /// </summary>
    Task<int> SoftDeleteSubtreeAsync(int id, long version, CancellationToken cancellationToken);

    Task AddRangeAsync(IEnumerable<Element> elements, CancellationToken cancellationToken);

    /// <summary>Removes every row (including soft-deleted) and restarts the identity sequence.</summary>
    Task ClearAllAsync(CancellationToken cancellationToken);

    /// <summary>Aligns the identity sequence with MAX(id) after rows were inserted with explicit ids.</summary>
    Task SyncIdentitySequenceAsync(CancellationToken cancellationToken);

    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

/// <summary>A database transaction managed by the repository.</summary>
public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
