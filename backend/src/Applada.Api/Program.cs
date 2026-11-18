using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Applada")
    ?? throw new InvalidOperationException(
        "Connection string 'Applada' is required. Configure it with User Secrets for development or a secret manager for deployment.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.MapGet("/health/ready", async (
    NpgsqlDataSource dataSource,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return Results.Ok(new { status = "ready", database = "connected" });
    }
    catch (NpgsqlException exception)
    {
        logger.LogWarning(
            "Database readiness probe failed with {ExceptionType}.",
            exception.GetType().Name);
        return Results.Problem(
            title: "Database unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
