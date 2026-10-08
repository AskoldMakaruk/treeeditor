using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TreeEditor.Infrastructure.Caching;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(AppDbContext db, RedisConnection redis, IOptions<RedisOptions> redisOptions) : ControllerBase
{
    private readonly RedisOptions _redisOptions = redisOptions.Value;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var databaseOk = await DatabaseReachableAsync(cancellationToken);
        var redis = RedisStatus();

        // Redis is optional; only Postgres being down makes the probe fail.
        var healthy = databaseOk && redis != "unavailable";

        var body = new
        {
            status = healthy ? "ok" : "degraded",
            database = databaseOk ? "connected" : "unavailable",
            redis,
        };

        return healthy ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }

    private async Task<bool> DatabaseReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    private string RedisStatus()
    {
        if (!_redisOptions.Enabled)
        {
            return "disabled";
        }

        try
        {
            return redis.Multiplexer?.IsConnected == true ? "connected" : "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }
}
