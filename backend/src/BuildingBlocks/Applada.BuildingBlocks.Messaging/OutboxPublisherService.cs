using Applada.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Chama <see cref="OutboxProcessor{TContext}.ProcessBatchAsync"/> em laço e espera
/// <see cref="OutboxOptions.PollInterval"/> quando o lote não publica nada. Lote que falha (banco fora, por
/// exemplo) dobra a espera a cada falha seguida, até <see cref="OutboxOptions.MaxLoopBackoff"/>; a primeira falha
/// loga Error, as seguintes Warning, e o primeiro lote bem-sucedido loga a recuperação e zera o backoff.
/// Desligado com <c>Messaging:PublisherEnabled=false</c> (padrão: ligado).
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

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var published = await processor.ProcessBatchAsync(stoppingToken);
                if (consecutiveFailures > 0)
                {
                    LogRecovered(logger, typeof(TContext).Name, consecutiveFailures);
                    consecutiveFailures = 0;
                }

                if (published > 0)
                {
                    continue;
                }

                delay = options.Value.PollInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Banco indisponível, por exemplo: tenta de novo depois do backoff.
                consecutiveFailures++;
                delay = FailureDelay(options.Value, consecutiveFailures);
                if (consecutiveFailures == 1)
                {
                    LogBatchFailed(logger, typeof(TContext).Name, delay, ex);
                }
                else
                {
                    LogBatchStillFailing(logger, typeof(TContext).Name, consecutiveFailures, delay, ex);
                }
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary><c>min(PollInterval × 2^failures, MaxLoopBackoff)</c>.</summary>
    private static TimeSpan FailureDelay(OutboxOptions options, int consecutiveFailures)
    {
        // Expoente limitado: além disso o resultado já passou de qualquer teto razoável (e não estoura o double).
        var delayTicks = options.PollInterval.Ticks * Math.Pow(2, Math.Min(consecutiveFailures, 30));
        return delayTicks >= options.MaxLoopBackoff.Ticks ? options.MaxLoopBackoff : TimeSpan.FromTicks((long)delayTicks);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Publicador do outbox {Outbox} desligado por configuração.")]
    private static partial void LogDisabled(ILogger logger, string outbox);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar lote do outbox {Outbox}; nova tentativa em {Delay}.")]
    private static partial void LogBatchFailed(ILogger logger, string outbox, TimeSpan delay, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lote do outbox {Outbox} continua falhando ({Failures} falhas seguidas); nova tentativa em {Delay}.")]
    private static partial void LogBatchStillFailing(ILogger logger, string outbox, int failures, TimeSpan delay, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox {Outbox} recuperado após {Failures} falhas seguidas.")]
    private static partial void LogRecovered(ILogger logger, string outbox, int failures);
}
