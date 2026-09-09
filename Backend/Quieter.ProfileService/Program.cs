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

app.MapGet("/internal/world/characters", (
    ProfileStore store,
    CancellationToken cancellationToken) => store.LoadWorldCharactersAsync(cancellationToken));

app.MapGet("/internal/worlds/{worldId:int}/resource-nodes", (
    int worldId,
    ProfileStore store,
    CancellationToken cancellationToken) =>
    store.LoadResourceNodeStatesAsync(worldId, cancellationToken));

app.MapPut("/internal/worlds/{worldId:int}/resource-nodes", async (
    int worldId,
    ResourceNodeStateListResponse request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveResourceNodeStatesAsync(worldId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/internal/worlds/{worldId:int}/placed-objects", (
    int worldId,
    ProfileStore store,
    CancellationToken cancellationToken) =>
    store.LoadPlacedObjectsAsync(worldId, cancellationToken));

app.MapPut("/internal/worlds/{worldId:int}/placed-objects", async (
    int worldId,
    PlacedObjectListResponse request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SavePlacedObjectsAsync(worldId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

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

app.MapPut("/internal/players/{steamId}/inventory", async (
    string steamId,
    InventoryRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveInventoryAsync(steamId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/snapshot", async (
    string steamId, PlayerSnapshotRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveSnapshotAsync(steamId, request, cancellationToken)
            ? Results.NoContent() : Results.NotFound();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Character revision or account binding changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/world/characters/{characterId}/snapshot", async (
    string characterId, PlayerSnapshotRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (!string.Equals(characterId, request.CharacterId, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Character identifiers disagree." });
        return await store.SaveDetachedCharacterAsync(request, cancellationToken)
            ? Results.NoContent() : Results.NotFound();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Character revision or ownership changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/players/{steamId}/new-stranger", async (
    string steamId, NewStrangerRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.CreateNewStrangerAsync(steamId, request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Character changed before the new life was accepted." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/heir", async (
    string steamId, RegisterHeirRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.RegisterHeirAsync(steamId, request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Estate registration changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/players/{steamId}/assume-heir", async (
    string steamId, AssumeHeirRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.AssumeRegisteredHeirAsync(
            steamId, request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Inheritance transition changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/players/{steamId}/heir-offers", async (
    string steamId, CreateHeirOfferRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.CreateHeirOfferAsync(steamId, request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Heir donation state changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/internal/players/{steamId}/heir-offer", async (
    string steamId, ProfileStore store, CancellationToken cancellationToken) =>
{
    try
    {
        var offer = await store.GetPendingHeirOfferAsync(steamId, cancellationToken);
        return offer is null ? Results.NoContent() : Results.Ok(offer);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/players/{steamId}/heir-offers/{offerId:guid}/accept", async (
    string steamId, Guid offerId, AcceptHeirOfferRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.AcceptHeirOfferAsync(
            steamId, offerId, request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Heir offer acceptance changed." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/world/character-transfer", async (
    CharacterPairSnapshotRequest request, ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveCharacterPairAsync(request, cancellationToken)
            ? Results.NoContent() : Results.NotFound();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Character state changed during item transfer." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/internal/world/npcs", async (
    CreateWorldNpcRequest request, ProfileStore store, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.CreateWorldNpcAsync(request, cancellationToken));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Character identifier is already controlled." });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/survival", async (
    string steamId,
    SurvivalRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveSurvivalAsync(steamId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/worlds/{worldId:int}/deposit-knowledge", async (
    string steamId,
    int worldId,
    DepositKnowledgeListResponse request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveDepositKnowledgeAsync(
                steamId, worldId, request, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/internal/players/{steamId}/worlds/{worldId:int}/map-notes", async (
    string steamId,
    int worldId,
    MapNoteListResponse request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    try
    {
        return await store.SaveMapNotesAsync(steamId, worldId, request, cancellationToken)
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
