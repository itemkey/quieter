using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quieter.ProfileService.Contracts;
using Quieter.ProfileService.Data;

var builder = WebApplication.CreateBuilder(args);
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = Required(builder.Configuration, "Postgres:Host"),
        Port = builder.Configuration.GetValue("Postgres:Port", 5432),
        Database = Required(builder.Configuration, "Postgres:Database"),
        Username = Required(builder.Configuration, "Postgres:Username"),
        Password = Secret(builder.Configuration, "Postgres:Password", "Postgres:PasswordFile"),
    }.ConnectionString;
}

var internalToken = Secret(builder.Configuration, "InternalToken", "InternalTokenFile");

builder.Services.AddDbContext<ProfileDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ProfileStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/internal")
        && !string.Equals(
            context.Request.Headers["X-Quieter-Internal-Token"],
            internalToken,
            StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }

    await next();
});

app.MapGet("/health", async (ProfileDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "healthy" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapGet("/internal/world/current", (
    ProfileStore store,
    CancellationToken cancellationToken) => store.GetOrCreateWorldAsync(cancellationToken));

app.MapPost("/internal/players/login", async (
    PlayerLoginRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.LoginAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/position", async (
    string steamId,
    PositionRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SavePositionAsync(steamId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
    await database.Database.MigrateAsync();
}

if (migrateOnly)
{
    return;
}

await app.RunAsync();

static string Required(IConfiguration configuration, string key)
{
    return configuration[key]
        ?? throw new InvalidOperationException($"{key} is required.");
}

static string Secret(IConfiguration configuration, string valueKey, string fileKey)
{
    var path = configuration[fileKey];
    if (!string.IsNullOrWhiteSpace(path))
    {
        return File.ReadAllText(path).Trim();
    }

    return Required(configuration, valueKey);
}

public partial class Program;
