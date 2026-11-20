using Applada.Api.Errors;
using Applada.Api.Health;
using Applada.Api.OpenApi;
using Applada.Api.Security;
using Applada.BuildingBlocks.Messaging;
using Applada.Modules.Communities;
using Applada.Modules.Identity;
using Applada.Modules.Matches.Api;
using Applada.Modules.Profiles;
using Applada.ServiceDefaults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.AddApladaAuthentication();
builder.Services.AddApladaProblemDetails();
builder.Services.AddApladaRateLimiting(builder.Configuration);
builder.Services.AddRabbitMqPublisher(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProfilesModule(builder.Configuration);
builder.Services.AddCommunitiesModule(builder.Configuration);
builder.Services.AddMatchesModule(builder.Configuration);
builder.AddApladaDevelopmentOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: [Extensions.ReadyTag], timeout: TimeSpan.FromSeconds(5))
    // Broker fora não tira a API do balanceador: escritas seguem e o outbox publica quando ele voltar.
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded, tags: [Extensions.ReadyTag], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.UseApladaProblemDetails();
app.UseAuthentication();
app.UseCurrentUser();
app.UseAuthorization();
// Depois da autorização e da resolução do usuário: a partição é o id interno, e anônimo já recebeu 401.
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapIdentityEndpoints();
app.MapProfilesEndpoints();
app.MapCommunitiesEndpoints();
app.MapMatchesEndpoints();

// OpenAPI e Scalar só existem em Development; o teste DevelopmentSurfaceTests garante a ausência fora dele.
if (app.Environment.IsDevelopment())
{
    app.MapApladaDevelopmentApiReference();
}

app.Run();

public partial class Program;
