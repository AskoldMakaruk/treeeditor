namespace TreeEditor.Application.Caching;

/// <summary>Central place for cache key shapes so invalidation stays consistent.</summary>
public static class CacheKeys
{
    public const string TreePrefix = "tree:";
    public const string Roots = TreePrefix + "roots";
    public const string ApplyLock = "lock:apply";
    public const string ResetLock = "lock:reset";

    public static string Children(int parentId) => $"{TreePrefix}children:{parentId}";

    public static string Element(int id) => $"{TreePrefix}element:{id}";
}
