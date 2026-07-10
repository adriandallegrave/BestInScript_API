using BestInScript.API.Engine;
using BestInScript.API.Overlay;
using BestInScript.API.Persistence;
using BestInScript.API.Services;
using BestInScript.API.Tray;
using Microsoft.OpenApi.Models;

// Single-instance gate (BACKLOG 6.2): if the app is already running, signal that instance
// to open its UI and exit now — before the web host binds Kestrel's port, avoiding a crash.
var singleInstance = SingleInstanceGuard.Acquire();
if (!singleInstance.IsPrimary)
    return; // another instance is live; we signaled it to open its UI

var builder = WebApplication.CreateBuilder(args);

// Registered so the tray service can attach its activation listener and the DI container
// owns disposal (releasing the mutex on shutdown). Dispose is idempotent.
builder.Services.AddSingleton(singleInstance);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BestInScript API",
        Version = "v1",
        Description = "ARPG macro engine – assign keyboard scripts to toggle keys."
    });
});

// Repos are registered as concrete singletons and their interfaces forwarded to the
// same instance, so ProfileManager (via IProfileScopedStore) repoints the exact stores
// that controllers, the validator, and the engine all use.
builder.Services.AddSingleton<ScriptRepository>();
builder.Services.AddSingleton<IScriptRepository>(sp => sp.GetRequiredService<ScriptRepository>());
builder.Services.AddSingleton<IProfileScopedStore>(sp => sp.GetRequiredService<ScriptRepository>());
builder.Services.AddSingleton<PresetRepository>();
builder.Services.AddSingleton<IPresetRepository>(sp => sp.GetRequiredService<PresetRepository>());
builder.Services.AddSingleton<IProfileScopedStore>(sp => sp.GetRequiredService<PresetRepository>());
builder.Services.AddSingleton<ProfileManager>();
builder.Services.AddSingleton<IInputSimulator, InputSimulatorService>();
builder.Services.AddSingleton<IScreenSampler, ScreenColorService>();
builder.Services.AddSingleton<PixelCaptureService>();
builder.Services.AddSingleton<IDelayScheduler, TaskDelayScheduler>();
builder.Services.AddSingleton<IRandomSource, SharedRandomSource>();
builder.Services.AddSingleton<ConfigValidator>();
builder.Services.AddSingleton<IScriptRunner, ScriptExecutor>();
builder.Services.AddSingleton<KeyboardHook>();
builder.Services.AddSingleton<ScriptCoordinator>();
builder.Services.AddSingleton<HotkeyEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HotkeyEngine>());

// ── On-screen overlay ──────────────────────────────────────────────────────
// OverlaySettingsStore: holds + persists the settings (read/written by OverlayController).
// OverlayHostedService: spins up the WPF window on a dedicated STA thread.
// Both are required — without the hosted service no overlay is created on screen.
builder.Services.AddSingleton<OverlaySettingsStore>();
builder.Services.AddHostedService<OverlayHostedService>();

// ── Diablo 4 event timers (world boss / helltide / legion) ──────────────────
// One startup HTTP fetch of the public schedule, cached in memory; the overlay
// polls it for live countdowns. This is the app's only outbound network call.
builder.Services.AddSingleton<EventScheduleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventScheduleService>());

// ── System tray icon ───────────────────────────────────────────────────────
// Quick actions while the app runs in the background: open UI, stop-all, exit.
builder.Services.AddHostedService<TrayIconHostedService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BestInScript v1");
    c.RoutePrefix = "swagger";
});

// Serve index.html from wwwroot (no static-file middleware; see WebAssetLocator).
app.MapGet("/", async (HttpContext ctx) =>
{
    var path = WebAssetLocator.Find("index.html");
    if (path is null)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync(
            "index.html not found. Place it at: wwwroot/index.html in the project root.");
        return;
    }

    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(path);
});

// Browser-tab icon, requested automatically by browsers and referenced
// explicitly by index.html's <link rel="icon">.
app.MapGet("/favicon.ico", async (HttpContext ctx) =>
{
    var path = WebAssetLocator.Find("app.ico");
    if (path is null)
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    ctx.Response.ContentType = "image/x-icon";
    await ctx.Response.SendFileAsync(path);
});

app.UseAuthorization();
app.MapControllers();
app.Run();