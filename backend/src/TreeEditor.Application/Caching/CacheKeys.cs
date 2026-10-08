namespace TreeEditor.Application.Caching;

/// <summary>Central place for cache key shapes so invalidation stays consistent.</summary>
public static class CacheKeys
{
    public const string TreePrefix = "tree:";
    public const string Roots = TreePrefix + "roots";

    /// <summary>Single lock serialising all tree writers (Apply and Reset).</summary>
    public const string TreeLock = "lock:tree";

    public static string Children(int parentId) => $"{TreePrefix}children:{parentId}";

    public static string Element(int id) => $"{TreePrefix}element:{id}";
}
