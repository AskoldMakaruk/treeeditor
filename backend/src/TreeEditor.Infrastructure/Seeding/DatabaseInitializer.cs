using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TreeEditor.Application.Interfaces;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Infrastructure.Seeding;

public sealed class DatabaseInitializer(
    AppDbContext db,
    IElementRepository repository,
    ISampleDataProvider sampleData,
    ILogger<DatabaseInitializer> logger)
    : IDatabaseInitializer
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxAttempts = 30;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await MigrateWithRetryAsync(cancellationToken);

        var hasAnyRow = await db.Elements.IgnoreQueryFilters().AnyAsync(cancellationToken);
        if (hasAnyRow)
        {
            return;
        }

        logger.LogInformation("Seeding sample data...");
        var revision = await repository.GetRevisionAsync(cancellationToken);
        if (revision == 0)
        {
            revision = 1;
        }

        var sample = sampleData.GetSampleData();
        foreach (var element in sample)
        {
            element.Version = revision;
        }

        await repository.BulkInsertAsync(sample, cancellationToken);
        await repository.SyncIdentitySequenceAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for PostgreSQL to accept connections before migrating. `devenv up` starts all
    /// processes at once, so the API can boot before the database is ready; without this the
    /// host would crash and be restarted in a loop.
    /// </summary>
    private async Task MigrateWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Database not ready (attempt {Attempt}/{Max}); retrying in {Delay}s...",
                    attempt,
                    MaxAttempts,
                    RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
