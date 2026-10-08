namespace TreeEditor.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string Section = "Redis";

    public string Configuration { get; set; } = "localhost:6379";

    /// <summary>When false the app runs without Redis (cache is a no-op, locks fall back to in-process).</summary>
    public bool Enabled { get; set; } = true;
}
