using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Applada.BuildingBlocks.Persistence;

public static class MigrationsOnStartup
{
    /// <summary>Chave de configuração que liga as migrations na subida (dev e testes; produção usa bundle).</summary>
    public const string ConfigurationKey = "Database:ApplyMigrationsOnStartup";

    /// <summary>
    /// Registra a aplicação das migrations de <typeparamref name="TContext"/> na subida, antes de
    /// qualquer hosted service iniciar, quando <c>Database:ApplyMigrationsOnStartup</c> for <c>true</c>.
    /// Cada módulo chama uma vez para o seu <c>DbContext</c>.
    /// </summary>
    public static IServiceCollection AddMigrationsOnStartup<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<MigrateOnStartupService<TContext>>();
        return services;
    }
}

internal sealed partial class MigrateOnStartupService<TContext>(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MigrateOnStartupService<TContext>> logger) : IHostedLifecycleService
    where TContext : DbContext
{
    // StartingAsync roda antes do StartAsync de todos os hosted services, inclusive o servidor web.
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>(MigrationsOnStartup.ConfigurationKey))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, typeof(TContext).Name);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations de {Context} aplicadas na subida.")]
    private static partial void LogMigrated(ILogger logger, string context);
}
