using System.Text.Json;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Providers;
using MobaDev.Dashboard.Api.Security;
using MobaDev.Dashboard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var dashboardSection = builder.Configuration.GetSection(DashboardOptions.SectionName);
var configuredOptions = dashboardSection.Get<DashboardOptions>() ?? new DashboardOptions();
var safeBindUrl = LoopbackBindingGuard.EnsureLoopbackUrl(configuredOptions.BindUrl);

if (!string.Equals(builder.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
{
    builder.WebHost.UseUrls(safeBindUrl);
}

builder.Services.Configure<DashboardOptions>(dashboardSection);
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
});

builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<ClaudeMonitorProvider>();
builder.Services.AddSingleton<CodexMonitorProvider>();
builder.Services.AddSingleton<AntigravityMonitorProvider>();
builder.Services.AddSingleton<GovernanceProvider>();
builder.Services.AddSingleton<ConfigDocsProvider>();
builder.Services.AddSingleton<PipelineProvider>();
builder.Services.AddSingleton<WorktreeProvider>();
builder.Services.AddSingleton<DashboardSnapshotService>();

var app = builder.Build();

app.UseCors("LocalFrontendOnly");

var resolvedPaths = configuredOptions.ResolvePaths();
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

app.Run();

public partial class Program;
