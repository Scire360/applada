using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Applada.Api.IntegrationTests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _environment;
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();

    public ApiFactory()
        : this("Production")
    {
    }

    protected ApiFactory(string environment) => _environment = environment;

    public MutableTimeProvider Clock { get; } = new();

    public string RabbitMqConnectionString => _rabbit.GetConnectionString();

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

    public HttpClient CreateClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Issue(subject, now: Clock.GetUtcNow()));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:applada", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", _rabbit.GetConnectionString());
        builder.UseSetting("Authentication:Authority", TestJwt.Issuer);
        builder.UseSetting("Authentication:Audience", TestJwt.Audience);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Messaging:PublisherEnabled", "false");
        builder.UseSetting("Matches:WriteRateLimitPerMinute", "10000");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter, ThrowingStartupFilter>();

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Sem descoberta OIDC: a configuração vem pronta, sem rede.
                var configuration = new OpenIdConnectConfiguration { Issuer = TestJwt.Issuer };
                configuration.SigningKeys.Add(TestJwt.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.IssuerSigningKey = TestJwt.SigningKey;
                // O JwtBearer valida lifetime com o relógio do sistema, não com o TimeProvider da DI.
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = Clock.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore.Value - parameters.ClockSkew <= now)
                        && (expires is null || now < expires.Value + parameters.ClockSkew);
                };
                options.TokenValidationParameters.ValidIssuer = TestJwt.Issuer;
                options.TokenValidationParameters.ValidAudience = TestJwt.Audience;
            });
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
