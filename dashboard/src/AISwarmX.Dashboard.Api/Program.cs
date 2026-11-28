using System.Text.Json;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Hubs;
using AISwarmX.Dashboard.Api.Providers;
using AISwarmX.Dashboard.Api.Security;
using AISwarmX.Dashboard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var dashboardSection = builder.Configuration.GetSection(DashboardOptions.SectionName);
if (!string.Equals(builder.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
{
    var startupExplicitKeys = DashboardOptions.CaptureExplicitOverrideKeys(builder.Configuration);
    var startupOptions = (dashboardSection.Get<DashboardOptions>() ?? new DashboardOptions())
        .ResolveWithSwarmConfig(explicitOverrideKeys: startupExplicitKeys);
    var safeBindUrl = LoopbackBindingGuard.EnsureLoopbackUrl(startupOptions.BindUrl);
    builder.WebHost.UseUrls(safeBindUrl);
}

builder.Services.Configure<DashboardOptions>(dashboardSection);
builder.Services.Configure<DashboardOptions>(options =>
{
    var explicitKeys = DashboardOptions.CaptureExplicitOverrideKeys(builder.Configuration);
    options.ApplySwarmConfigInPlace(explicitOverrideKeys: explicitKeys);
});
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddCors(cors =>
{
    cors.AddPolicy("LocalFrontendOnly", policy =>
    {
        policy
            .WithOrigins(
                "http://127.0.0.1:5173",
                "http://localhost:5173",
                "http://127.0.0.1:5179",
                "http://localhost:5179")
            .WithMethods(HttpMethods.Get)
            .AllowAnyHeader();
    });

    cors.AddPolicy("LocalSignalROnly", policy =>
    {
        policy
            .WithOrigins(
                "http://127.0.0.1:5173",
                "http://localhost:5173",
                "http://127.0.0.1:5179",
                "http://localhost:5179")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSignalR().AddJsonProtocol(json =>
{
    json.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<ClaudeMonitorProvider>();
builder.Services.AddSingleton<CodexMonitorProvider>();
builder.Services.AddSingleton<AntigravityMonitorProvider>();
builder.Services.AddSingleton<GovernanceProvider>();
builder.Services.AddSingleton<ConfigDocsProvider>();
builder.Services.AddSingleton<PipelineProvider>();
builder.Services.AddSingleton<WorktreeProvider>();
builder.Services.AddSingleton<SwarmBusProvider>();
builder.Services.AddSingleton<DashboardSnapshotService>();
builder.Services.AddHostedService<SwarmLiveBroadcastService>();

var app = builder.Build();

var allowedLoopbackOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "http://127.0.0.1:5173",
    "http://localhost:5173",
    "http://127.0.0.1:5179",
    "http://localhost:5179"
};

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        var host = context.Request.Host.Host;
        var isLoopbackHost = string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        if (!isLoopbackHost)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase) &&
            context.Request.Headers.TryGetValue("Origin", out var originValues))
        {
            var origin = originValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(origin) && !allowedLoopbackOrigins.Contains(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
    }

    await next();
});

app.UseCors("LocalFrontendOnly");

var effectiveOptions = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<DashboardOptions>>()
    .CurrentValue;
LoopbackBindingGuard.EnsureLoopbackUrl(effectiveOptions.BindUrl);
var resolvedPaths = effectiveOptions.ResolvePaths();
if (!string.IsNullOrWhiteSpace(resolvedPaths.StaticWebRootPath) &&
    Directory.Exists(resolvedPaths.StaticWebRootPath))
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(resolvedPaths.StaticWebRootPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

var api = app.MapGroup("/api");

api.MapGet("/health", async (DashboardSnapshotService service, CancellationToken ct) =>
    Results.Ok(await service.GetHealthAsync(ct)));

api.MapGet("/snapshot", async (DashboardSnapshotService service, bool? refresh, CancellationToken ct) =>
    Results.Ok(await service.GetSnapshotAsync(refresh ?? false, ct)));

api.MapGet("/monitors", async (DashboardSnapshotService service, bool? refresh, CancellationToken ct) =>
    Results.Ok(await service.GetMonitorsAsync(refresh ?? false, ct)));

api.MapGet("/governance", async (DashboardSnapshotService service, bool? refresh, CancellationToken ct) =>
    Results.Ok(await service.GetGovernanceAsync(refresh ?? false, ct)));

api.MapGet("/governance/diff", async (
    DashboardSnapshotService service,
    string tool,
    string kind,
    string fileName,
    CancellationToken ct) =>
{
    try
    {
        var diff = await service.GetRuleDiffAsync(tool, kind, fileName, ct);
        return diff is null ? Results.NotFound() : Results.Ok(diff);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/docs", async (
    DashboardSnapshotService service,
    string scope,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await service.GetConfigDocsCatalogAsync(scope, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/docs/content", async (
    DashboardSnapshotService service,
    string scope,
    string path,
    CancellationToken ct) =>
{
    try
    {
        var doc = await service.GetConfigDocContentAsync(scope, path, ct);
        return doc is null ? Results.NotFound() : Results.Ok(doc);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapGet("/pipeline", async (DashboardSnapshotService service, bool? refresh, CancellationToken ct) =>
    Results.Ok(await service.GetPipelineAsync(refresh ?? false, ct)));

api.MapGet("/hygiene", async (DashboardSnapshotService service, bool? refresh, CancellationToken ct) =>
    Results.Ok(await service.GetHygieneAsync(refresh ?? false, ct)));

api.MapGet("/bus", async (DashboardSnapshotService service, bool? refresh, HttpResponse response, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await service.GetSwarmBusAsync(refresh ?? false, ct));
    }
    catch (SwarmBusContentionException ex)
    {
        response.Headers.RetryAfter = "1";
        return Results.Problem(
            title: "Swarm bus refresh superseded by concurrent workspace change",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapHub<SwarmDashboardHub>("/hubs/swarm").RequireCors("LocalSignalROnly");

app.Run();

public partial class Program;
