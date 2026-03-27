using BestInScript.API.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Services ───────────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// Swagger / OpenAPI
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

// App services (singletons so the same instance is shared)
builder.Services.AddSingleton<ScriptRepository>();
builder.Services.AddSingleton<InputSimulatorService>();
builder.Services.AddSingleton<HotkeyEngine>();

// Register HotkeyEngine as IHostedService so StartAsync/StopAsync are called
builder.Services.AddHostedService(sp => sp.GetRequiredService<HotkeyEngine>());

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BestInScript v1");
    c.RoutePrefix = "swagger";
});

// Serve index.html at root — no service registration needed for static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

app.Run();