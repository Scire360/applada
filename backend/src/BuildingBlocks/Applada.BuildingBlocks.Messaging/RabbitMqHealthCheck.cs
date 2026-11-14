using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>Saudável quando a conexão do <see cref="RabbitMqEventPublisher"/> está (ou consegue ficar) aberta.</summary>
public sealed class RabbitMqHealthCheck(RabbitMqEventPublisher publisher) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await publisher.IsConnectedAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Conexão com o RabbitMQ fechada.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ indisponível.", ex);
        }
    }
}
