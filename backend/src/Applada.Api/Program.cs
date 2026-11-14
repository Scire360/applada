using Applada.Api.Errors;
using Applada.Api.Health;
using Applada.Api.Security;
using Applada.BuildingBlocks.Messaging;
using Applada.Modules.Identity;
using Applada.Modules.Matches.Infrastructure;
using Applada.ServiceDefaults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.AddApladaAuthentication();
builder.Services.AddApladaProblemDetails();
builder.Services.AddRabbitMqPublisher(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddMatchesInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: [Extensions.ReadyTag], timeout: TimeSpan.FromSeconds(5))
    // Broker fora não tira a API do balanceador: escritas seguem e o outbox publica quando ele voltar.
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded, tags: [Extensions.ReadyTag], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.UseApladaProblemDetails();
app.UseAuthentication();
app.UseCurrentUser();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapIdentityEndpoints();

app.Run();

public partial class Program;
