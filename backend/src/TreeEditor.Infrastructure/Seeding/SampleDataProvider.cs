using System.Text.Json;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Infrastructure.Seeding;

/// <summary>
/// Loads the initial sample data from <c>data/sample-tree.json</c>, which is embedded into this
/// assembly (the file is the source of truth in the repo; see <c>scripts/build-sample-tree.py</c>).
/// It holds ~13k elements forming a 10-level hierarchy, so lazy loading can be exercised at scale.
/// </summary>
public sealed class SampleDataProvider : ISampleDataProvider
{
    private const string ResourceName = "TreeEditor.Infrastructure.Seeding.sample-tree.json";

    private static readonly DateTimeOffset Timestamp = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<IReadOnlyList<SampleNode>> Data =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<Element> GetSampleData() =>
        Data.Value
            .Select(node => new Element
            {
                Id = node.Id,
                Value = node.Value,
                ParentId = node.ParentId,
                IsDeleted = false,
                CreatedAt = Timestamp,
                UpdatedAt = Timestamp,
            })
            .ToList();

    private static IReadOnlyList<SampleNode> Load()
    {
        using var stream = typeof(SampleDataProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");

        var file = JsonSerializer.Deserialize<SampleTreeFile>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("sample-tree.json could not be parsed.");

        return file.Nodes ?? throw new InvalidOperationException("sample-tree.json has no nodes.");
    }

    private sealed record SampleNode(int Id, string Value, int? ParentId);

    private sealed record SampleTreeFile(string? Source, int Count, IReadOnlyList<SampleNode>? Nodes);
}
