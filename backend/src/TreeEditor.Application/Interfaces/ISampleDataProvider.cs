using TreeEditor.Domain.Entities;

namespace TreeEditor.Application.Interfaces;

/// <summary>Supplies the deterministic sample tree used for initial seeding and Reset.</summary>
public interface ISampleDataProvider
{
    IReadOnlyList<Element> GetSampleData();
}

/// <summary>Applies migrations and seeds the sample data when the database is empty.</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
