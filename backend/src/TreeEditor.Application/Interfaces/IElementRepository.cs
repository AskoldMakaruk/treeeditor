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

    /// <summary>
    /// Updates the values of the given non-deleted elements in a single statement. Returns the
    /// number of affected rows; callers validate existence beforehand.
    /// </summary>
    Task<int> UpdateValuesAsync(
        IReadOnlyList<Dtos.UpdateOperation> updates,
        long version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a batch of new elements in one round trip and returns the generated id for each
    /// temporary id. A negative <c>ParentId</c> refers to another addition in the same batch
    /// (resolved via the parent navigation, so batch order does not matter).
    /// </summary>
    Task<IReadOnlyList<Dtos.AddedElementResult>> AddRangeAsync(
        IReadOnlyList<Dtos.AddOperation> additions,
        long version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes an element and, recursively, every descendant — including descendants that were
    /// never materialised in memory. Returns the number of affected rows. Does not touch the parent
    /// (callers batch parent bumps through <see cref="TouchManyAsync"/>).
    /// </summary>
    Task<int> SoftDeleteSubtreeAsync(int id, long version, CancellationToken cancellationToken);

    /// <summary>Stamps the given (still existing) rows with a new version in one statement.</summary>
    Task TouchManyAsync(IReadOnlyList<int> ids, long version, CancellationToken cancellationToken);

    /// <summary>Bulk-inserts rows with explicit ids (seeding / reset).</summary>
    Task BulkInsertAsync(IEnumerable<Element> elements, CancellationToken cancellationToken);

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
