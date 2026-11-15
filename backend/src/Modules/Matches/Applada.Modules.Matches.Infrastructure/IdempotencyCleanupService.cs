using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Roda <see cref="IdempotencyRetentionCleanup"/> na subida e depois a cada <see cref="Interval"/>. Falha (banco fora,
/// por exemplo) só loga: a próxima passada apaga o que ficou. Várias réplicas podem rodar ao mesmo tempo sem problema
/// (o DELETE é idempotente). Desligado com <c>Matches:IdempotencyCleanupEnabled=false</c> (padrão: ligado).
/// </summary>
internal sealed partial class IdempotencyCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    public const string EnabledKey = "Matches:IdempotencyCleanupEnabled";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(EnabledKey, defaultValue: true))
        {
            LogDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<IdempotencyRetentionCleanup>().DeleteExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    LogDeleted(logger, deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogFailed(logger, Interval, ex);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Limpeza de idempotency_keys de Matches desligada por configuração.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} respostas de idempotência vencidas apagadas de Matches.")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha na limpeza de idempotency_keys de Matches; nova tentativa em {Interval}.")]
    private static partial void LogFailed(ILogger logger, TimeSpan interval, Exception exception);
}
