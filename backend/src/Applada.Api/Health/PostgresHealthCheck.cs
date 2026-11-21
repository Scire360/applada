using Applada.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Applada.Api.Health;

/// <summary>Saudável quando o PostgreSQL aceita conexão (via contexto do Identity, que sempre existe).</summary>
internal sealed class PostgresHealthCheck(IdentityDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL indisponível.");
}
