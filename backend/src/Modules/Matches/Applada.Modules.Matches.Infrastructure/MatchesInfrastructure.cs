using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Matches.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Applada.Modules.Matches.Infrastructure;

public static class MatchesInfrastructure
{
    private const string ConnectionStringName = "applada";

    /// <summary>
    /// Registra o <see cref="MatchesDbContext"/>, a unidade de trabalho, as migrations na subida e o publicador do
    /// outbox de Matches. Exige um <c>IIntegrationEventPublisher</c> registrado (como no Identity).
    /// </summary>
    public static IServiceCollection AddMatchesInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Connection string lida na criação do contexto, não no registro.
        services.AddDbContext<MatchesDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} não configurada."),
            MatchesDbContext.ConfigureNpgsql));

        services.AddOptions<MatchesPersistenceOptions>()
            .Bind(configuration.GetSection(MatchesPersistenceOptions.SectionName))
            .Validate(
                o => o.LockTimeout >= MatchesPersistenceOptions.MinLockTimeout && o.LockTimeout <= MatchesPersistenceOptions.MaxLockTimeout,
                "Matches:LockTimeout deve ficar entre 1 ms e 1 minuto.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMatchesUnitOfWork, MatchesUnitOfWork>();
        services.AddMigrationsOnStartup<MatchesDbContext>();
        services.AddOutboxPublisher<MatchesDbContext>();

        return services;
    }
}
