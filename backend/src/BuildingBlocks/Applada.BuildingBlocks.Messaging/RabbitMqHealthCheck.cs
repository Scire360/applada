using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Saudável quando a conexão do <see cref="RabbitMqEventPublisher"/> está (ou consegue ficar) aberta. Em
/// falha devolve o <c>failureStatus</c> do registro: a API registra como <c>Degraded</c>, porque com o
/// broker fora ela continua aceitando escritas e o outbox guarda os eventos.
/// </summary>
public sealed class RabbitMqHealthCheck(RabbitMqEventPublisher publisher) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return await publisher.IsConnectedAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, "Conexão com o RabbitMQ fechada.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ indisponível.", ex);
        }
    }
}
