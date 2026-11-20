using Applada.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Applada.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private const string ConnectionStringName = "rabbitmq";

    /// <summary>
    /// Registra o <see cref="RabbitMqEventPublisher"/> (singleton) como <see cref="IIntegrationEventPublisher"/>.
    /// A connection string <c>ConnectionStrings:rabbitmq</c> é lida na primeira resolução, não no registro.
    /// </summary>
    public static IServiceCollection AddRabbitMqPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(provider => new RabbitMqEventPublisher(
            new Uri(configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} não configurada.")),
            provider.GetRequiredService<ILogger<RabbitMqEventPublisher>>()));
        services.TryAddSingleton<IIntegrationEventPublisher>(provider => provider.GetRequiredService<RabbitMqEventPublisher>());

        return services;
    }

    /// <summary>
    /// Registra o processador e o <c>BackgroundService</c> do outbox de <typeparamref name="TContext"/>.
    /// Cada módulo com outbox chama uma vez. Exige um <see cref="IIntegrationEventPublisher"/> registrado.
    /// </summary>
    public static IServiceCollection AddOutboxPublisher<TContext>(this IServiceCollection services)
        where TContext : DbContext, IHasOutbox
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<OutboxOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxMetrics>();
        services.TryAddSingleton<OutboxProcessor<TContext>>();
        services.AddHostedService<OutboxPublisherService<TContext>>();

        return services;
    }
}
