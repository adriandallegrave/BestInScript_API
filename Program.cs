using System.IO;
using BestInScript.API.Overlay;
using BestInScript.API.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<ScriptRepository>();
builder.Services.AddSingleton<InputSimulatorService>();
builder.Services.AddSingleton<HotkeyEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HotkeyEngine>());

// ── On-screen overlay ──────────────────────────────────────────────────────
builder.Services.AddSingleton<OverlaySettingsStore>();
builder.Services.AddHostedService<OverlayHostedService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BestInScript v1");
    c.RoutePrefix = "swagger";
});

// ── Serve index.html with the overlay-settings panel auto-injected ─────────
// AppContext.BaseDirectory points to bin\Debug\net10.0-windows\ at runtime,
// so we walk up to the project root (where wwwroot lives) and also fall back
// to a sibling wwwroot for published builds.
app.MapGet("/", async (HttpContext ctx) =>
{
    var indexCandidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "index.html"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
    };
    var indexPath = indexCandidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    if (indexPath is null)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync(
            "index.html not found. Place it at: wwwroot/index.html in the project root.");
        return;
    }

    // Partial lives next to index.html.
    var dir = Path.GetDirectoryName(indexPath)!;
    var partialPath = Path.Combine(dir, "_overlay-panel.html");

    var html = await File.ReadAllTextAsync(indexPath);

    // Idempotent injection: only splice in if our marker isn't already present
    // (so a manually-pasted copy of the panel won't get duplicated).
    const string marker = "BIS_OVERLAY_PANEL_MARKER";
    if (File.Exists(partialPath) && !html.Contains(marker))
    {
        var partial = await File.ReadAllTextAsync(partialPath);

        // Splice right before </body>; fall back to end-of-document if absent.
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = idx >= 0
            ? html.Insert(idx, partial + Environment.NewLine)
            : html + Environment.NewLine + partial;
    }

    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(html);
});

app.UseAuthorization();
app.MapControllers();
app.Run();