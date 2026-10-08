using StackExchange.Redis;
using TreeEditor.Api.Realtime;
using TreeEditor.Application.Interfaces;
using TreeEditor.Domain;
using TreeEditor.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

// Real-time change propagation. A Redis backplane makes notifications reach clients connected
// to other API instances; without Redis it still works for a single instance.
var signalR = builder.Services.AddSignalR();
var redisConfiguration = builder.Configuration["Redis:Configuration"];
var redisEnabled = builder.Configuration.GetValue("Redis:Enabled", true);
if (redisEnabled && !string.IsNullOrWhiteSpace(redisConfiguration))
{
    signalR.AddStackExchangeRedis(redisConfiguration, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("treeeditor");
    });
}

// Per-connection visible-node tracking; used to scope change notifications.
builder.Services.AddSingleton<TreeVisibilityTracker>();

// Overrides the no-op registered by AddInfrastructure.
builder.Services.AddSingleton<ITreeChangeNotifier, SignalRTreeChangeNotifier>();

const string FrontendCors = "frontend";
builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors(FrontendCors);

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TreeEditor.Api");

// Maps domain and lock failures to meaningful status codes, logs everything, and turns
// unexpected failures into a 500 ProblemDetails response.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The client went away; nothing to report.
    }
    catch (DomainException ex)
    {
        logger.LogWarning(ex, "Domain error handling {Method} {Path}", context.Request.Method, context.Request.Path);
        await Results.Problem(title: "Invalid operation", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest)
            .ExecuteAsync(context);
    }
    catch (LockUnavailableException ex)
    {
        logger.LogWarning(ex, "Lock unavailable handling {Method} {Path}", context.Request.Method, context.Request.Path);
        await Results.Problem(title: "Conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict)
            .ExecuteAsync(context);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception handling {Method} {Path}", context.Request.Method, context.Request.Path);
        if (!context.Response.HasStarted)
        {
            await Results.Problem(title: "Server error", detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
        }
    }
});

app.MapControllers();
app.MapHub<TreeHub>("/hubs/tree");

app.Run();

public partial class Program;
