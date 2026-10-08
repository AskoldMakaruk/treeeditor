using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Infrastructure.Persistence;

public sealed class EfElementRepository : IElementRepository
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public EfElementRepository(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    private sealed record NodeRow(int Id, string Value, int? ParentId, bool HasChildren, long Version);

    private static ElementNodeDto Map(NodeRow row) =>
        new(row.Id, row.Value, row.ParentId, row.HasChildren, row.Version);

    public async Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken)
    {
        var rows = await Project(_db.Elements.AsNoTracking().Where(e => e.ParentId == null))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.Id).Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken)
    {
        var rows = await Project(_db.Elements.AsNoTracking().Where(e => e.ParentId == parentId))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.Id).Select(Map).ToList();
    }

    public async Task<ElementNodeDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var row = await Project(_db.Elements.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    private IQueryable<NodeRow> Project(IQueryable<Element> source) =>
        source.Select(e => new NodeRow(
            e.Id,
            e.Value,
            e.ParentId,
            _db.Elements.Any(c => c.ParentId == e.Id),
            e.Version));

    public async Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        await _db.TreeRevisions.AsNoTracking()
            .Where(r => r.Id == TreeRevision.SingletonId)
            .Select(r => r.Revision)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> BumpRevisionAsync(CancellationToken cancellationToken)
    {
        var row = await _db.TreeRevisions.FirstOrDefaultAsync(r => r.Id == TreeRevision.SingletonId, cancellationToken);
        if (row is null)
        {
            row = new TreeRevision { Id = TreeRevision.SingletonId, Revision = 1 };
            _db.TreeRevisions.Add(row);
        }

        row.Revision += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return row.Revision;
    }

    public async Task<IReadOnlyList<NodeVersionDto>> GetVersionsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Elements.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new NodeVersionDto(e.Id, e.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> AddAsync(Element element, long version, CancellationToken cancellationToken)
    {
        element.Version = version;
        _db.Elements.Add(element);
        await _db.SaveChangesAsync(cancellationToken);

        // Adding a child changes the parent's children-set -> bump its version.
        if (element.ParentId is int parentId)
        {
            await TouchAsync(parentId, version, cancellationToken);
        }

        return element.Id;
    }

    public async Task<bool> UpdateValueAsync(int id, string value, long version, CancellationToken cancellationToken)
    {
        var element = await _db.Elements.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (element is null)
        {
            return false;
        }

        element.Value = value;
        element.Version = version;
        element.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SoftDeleteSubtreeAsync(int id, long version, CancellationToken cancellationToken)
    {
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             WITH RECURSIVE subtree AS (
                 SELECT id FROM elements WHERE id = {id} AND is_deleted = FALSE
                 UNION ALL
                 SELECT child.id
                 FROM elements child
                 INNER JOIN subtree parent ON child.parent_id = parent.id
                 WHERE child.is_deleted = FALSE
             )
             UPDATE elements
             SET is_deleted = TRUE, version = {version}, updated_at = NOW()
             WHERE id IN (SELECT id FROM subtree)
             """,
            cancellationToken);

        // Removing the subtree changes the (still existing) parent's children-set.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE elements SET version = {version}, updated_at = NOW() WHERE id = (SELECT parent_id FROM elements WHERE id = {id})",
            cancellationToken);

        _db.ChangeTracker.Clear();
        return affected;
    }

    /// <summary>
    /// Bulk-inserts rows with <c>COPY</c> (binary import) instead of EF change tracking, which keeps
    /// seeding/reset of thousands of elements fast. Rows are written parents-first (ordered by id;
    /// the sample data is numbered parent-before-child).
    /// </summary>
    public async Task AddRangeAsync(IEnumerable<Element> elements, CancellationToken cancellationToken)
    {
        var ordered = elements.OrderBy(element => element.Id).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY elements (id, value, parent_id, is_deleted, version, created_at, updated_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var element in ordered)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(element.Id, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(element.Value, NpgsqlDbType.Text, cancellationToken);
            if (element.ParentId is int parentId)
            {
                await writer.WriteAsync(parentId, NpgsqlDbType.Integer, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.WriteAsync(element.IsDeleted, NpgsqlDbType.Boolean, cancellationToken);
            await writer.WriteAsync(element.Version, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(element.CreatedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync(element.UpdatedAt, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE elements RESTART IDENTITY CASCADE",
            cancellationToken);
        _db.ChangeTracker.Clear();
    }

    public async Task SyncIdentitySequenceAsync(CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('elements', 'id'), COALESCE((SELECT MAX(id) FROM elements), 1))",
            cancellationToken);
    }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }

    private async Task TouchAsync(int id, long version, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE elements SET version = {version}, updated_at = NOW() WHERE id = {id}",
            cancellationToken);
    }
}
