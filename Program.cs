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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BestInScript v1");
    c.RoutePrefix = "swagger";
});

// Serve index.html from wwwroot folder next to the project file.
// AppContext.BaseDirectory points to bin\Debug\net10.0\ at runtime,
// so we walk up to the project root (where wwwroot lives).
app.MapGet("/", async (HttpContext ctx) =>
{
    // Try project root first (dotnet run), then beside the exe (published)
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "index.html"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
    };

    var path = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

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

app.UseAuthorization();
app.MapControllers();

app.Run();