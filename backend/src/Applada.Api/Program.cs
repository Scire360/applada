using Applada.BuildingBlocks.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Applada")
    ?? throw new InvalidOperationException(
        "Connection string 'Applada' is required. Configure it with User Secrets for development or a secret manager for deployment.");

builder.Services.AddSingleton(_ => new PostgresReadinessProbe(connectionString));

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.MapGet("/health/ready", async (
    PostgresReadinessProbe readinessProbe,
    CancellationToken cancellationToken) =>
{
    if (!await readinessProbe.IsReadyAsync(cancellationToken))
    {
        return Results.Problem(
            title: "Database unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { status = "ready", database = "connected" });
});

app.Run();
