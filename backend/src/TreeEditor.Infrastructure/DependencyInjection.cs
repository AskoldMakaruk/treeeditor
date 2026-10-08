using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Application.Interfaces;
using TreeEditor.Application.Services;
using TreeEditor.Infrastructure.Caching;
using TreeEditor.Infrastructure.Locking;
using TreeEditor.Infrastructure.Persistence;
using TreeEditor.Infrastructure.Seeding;

namespace TreeEditor.Infrastructure;

public static class DependencyInjection
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=treeeditor;Username=treeeditor;Password=treeeditor";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? DefaultConnectionString;

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.Section));
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IElementRepository, EfElementRepository>();
        services.AddScoped<ISampleDataProvider, SampleDataProvider>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<ITreeQueryService, TreeQueryService>();
        services.AddScoped<IApplyService, ApplyService>();
        services.AddScoped<IResetService, ResetService>();
        services.AddScoped<ISyncService, SyncService>();

        // Replaced by the SignalR implementation in the API host; no-op elsewhere (tests).
        services.AddSingleton<ITreeChangeNotifier, NullTreeChangeNotifier>();

        services.AddHostedService<DatabaseInitializationHostedService>();

        return services;
    }
}
