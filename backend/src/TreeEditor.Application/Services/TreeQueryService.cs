using TreeEditor.Application.Caching;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Application.Services;

/// <summary>
/// Read side of the tree. Results are wrapped in the server-side cache because the
/// browser still fetches one level at a time while expanding the database tree.
/// </summary>
public sealed class TreeQueryService(IElementRepository repository, ICacheService cache) : ITreeQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<List<ElementNodeDto>>(CacheKeys.Roots, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var roots = await repository.GetRootsAsync(cancellationToken);
        await cache.SetAsync(CacheKeys.Roots, roots.ToList(), CacheTtl, cancellationToken);
        return roots;
    }

    public async Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken)
    {
        var key = CacheKeys.Children(parentId);
        var cached = await cache.GetAsync<List<ElementNodeDto>>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var children = await repository.GetChildrenAsync(parentId, cancellationToken);
        await cache.SetAsync(key, children.ToList(), CacheTtl, cancellationToken);
        return children;
    }

    public async Task<ElementNodeDto?> GetElementAsync(int id, CancellationToken cancellationToken)
    {
        var key = CacheKeys.Element(id);
        var cached = await cache.GetAsync<ElementNodeDto>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var element = await repository.GetByIdAsync(id, cancellationToken);
        if (element is not null)
        {
            await cache.SetAsync(key, element, CacheTtl, cancellationToken);
        }

        return element;
    }
}
