using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Infrastructure.Seeding;

/// <summary>
/// Runs migrations and seeding when the host starts. Kept out of Program.cs so that
/// design-time tooling (dotnet ef) never touches the database.
/// </summary>
public sealed class DatabaseInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        logger.LogInformation("Applying migrations and ensuring sample data is present...");
        await initializer.InitializeAsync(cancellationToken);
        logger.LogInformation("Database is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
