using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Infrastructure.Persistence;

public sealed class EfElementRepository(AppDbContext db, TimeProvider clock) : IElementRepository
{
    private sealed record NodeRow(
        int Id,
        string Value,
        int? ParentId,
        bool HasChildren,
        long Version,
        DateTimeOffset UpdatedAt);

    private static ElementNodeDto Map(NodeRow row) =>
        new(row.Id, row.Value, row.ParentId, row.HasChildren, row.Version, row.UpdatedAt);

    public async Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken)
    {
        var rows = await Project(db.Elements.AsNoTracking().Where(e => e.ParentId == null))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.Id).Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken)
    {
        var rows = await Project(db.Elements.AsNoTracking().Where(e => e.ParentId == parentId))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.Id).Select(Map).ToList();
    }

    public async Task<ElementNodeDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var row = await Project(db.Elements.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    private IQueryable<NodeRow> Project(IQueryable<Element> source) =>
        source.Select(e => new NodeRow(
            e.Id,
            e.Value,
            e.ParentId,
            db.Elements.Any(c => c.ParentId == e.Id),
            e.Version,
            e.UpdatedAt));

    public async Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        await db.TreeRevisions.AsNoTracking()
            .Where(r => r.Id == TreeRevision.SingletonId)
            .Select(r => r.Revision)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> BumpRevisionAsync(CancellationToken cancellationToken)
    {
        var row = await db.TreeRevisions.FirstOrDefaultAsync(r => r.Id == TreeRevision.SingletonId, cancellationToken);
        if (row is null)
        {
            row = new TreeRevision { Id = TreeRevision.SingletonId, Revision = 1 };
            db.TreeRevisions.Add(row);
        }

        row.Revision += 1;
        await db.SaveChangesAsync(cancellationToken);
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

        return await db.Elements.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new NodeVersionDto(e.Id, e.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> UpdateValuesAsync(
        IReadOnlyList<UpdateOperation> updates,
        long version,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        var ids = updates.Select(update => update.Id).ToArray();
        var values = updates.Select(update => update.Value).ToArray();
        var now = clock.GetUtcNow();

        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE elements AS e
             SET value = u.value, version = {version}, updated_at = {now}
             FROM unnest({ids}::int[], {values}::text[]) AS u(id, value)
             WHERE e.id = u.id AND e.is_deleted = FALSE
             """,
            cancellationToken);

        db.ChangeTracker.Clear();
        return affected;
    }

    public async Task<IReadOnlyList<AddedElementResult>> AddRangeAsync(
        IReadOnlyList<AddOperation> additions,
        long version,
        CancellationToken cancellationToken)
    {
        if (additions.Count == 0)
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var byTempId = new Dictionary<int, Element>();
        var elements = new List<Element>(additions.Count);

        foreach (var addition in additions)
        {
            var element = new Element
            {
                Value = addition.Value,
                ParentId = addition.ParentId is int parentId && parentId >= 0 ? parentId : null,
                Version = version,
                CreatedAt = now,
                UpdatedAt = now,
            };

            byTempId[addition.TempId] = element;
            elements.Add(element);
        }

        // A negative parent id refers to another addition in this batch: link via the navigation
        // so EF orders the inserts and fills the generated foreign keys in one SaveChanges.
        foreach (var addition in additions)
        {
            if (addition.ParentId is int parentId && parentId < 0)
            {
                byTempId[addition.TempId].Parent = byTempId[parentId];
            }
        }

        db.Elements.AddRange(elements);
        await db.SaveChangesAsync(cancellationToken);

        return additions
            .Select(addition => new AddedElementResult(addition.TempId, byTempId[addition.TempId].Id))
            .ToList();
    }

    public async Task<int> SoftDeleteSubtreeAsync(int id, long version, CancellationToken cancellationToken)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
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

        db.ChangeTracker.Clear();
        return affected;
    }

    public async Task TouchManyAsync(IReadOnlyList<int> ids, long version, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE elements SET version = {version}, updated_at = NOW() WHERE id = ANY({distinct}::int[]) AND is_deleted = FALSE",
            cancellationToken);

        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Bulk-inserts rows with <c>COPY</c> (binary import) instead of EF change tracking, which keeps
    /// seeding/reset of thousands of elements fast. Rows are written parents-first (ordered by id;
    /// the sample data is numbered parent-before-child).
    /// </summary>
    public async Task BulkInsertAsync(IEnumerable<Element> elements, CancellationToken cancellationToken)
    {
        var ordered = elements.OrderBy(element => element.Id).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
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
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE elements RESTART IDENTITY CASCADE",
            cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task SyncIdentitySequenceAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('elements', 'id'), COALESCE((SELECT MAX(id) FROM elements), 1))",
            cancellationToken);
    }

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }
}
