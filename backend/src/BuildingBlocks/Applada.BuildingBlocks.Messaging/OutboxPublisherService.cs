using Applada.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Chama <see cref="OutboxProcessor{TContext}.ProcessBatchAsync"/> em laço e espera
/// <see cref="OutboxOptions.PollInterval"/> quando o lote não publica nada. Desligado com
/// <c>Messaging:PublisherEnabled=false</c> (padrão: ligado).
/// </summary>
public sealed partial class OutboxPublisherService<TContext>(
    OutboxProcessor<TContext> processor,
    IConfiguration configuration,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherService<TContext>> logger) : BackgroundService
    where TContext : DbContext, IHasOutbox
{
    public const string EnabledKey = "Messaging:PublisherEnabled";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(EnabledKey, defaultValue: true))
        {
            LogDisabled(logger, typeof(TContext).Name);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await processor.ProcessBatchAsync(stoppingToken) > 0)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Banco indisponível, por exemplo: tenta de novo no próximo ciclo.
                LogBatchFailed(logger, typeof(TContext).Name, ex);
            }

            try
            {
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicador do outbox {Outbox} desligado por configuração.")]
    private static partial void LogDisabled(ILogger logger, string outbox);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar lote do outbox {Outbox}.")]
    private static partial void LogBatchFailed(ILogger logger, string outbox, Exception exception);
}
