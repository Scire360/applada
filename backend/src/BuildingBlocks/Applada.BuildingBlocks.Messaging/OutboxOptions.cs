namespace Applada.BuildingBlocks.Messaging;

public sealed class OutboxOptions
{
    /// <summary>Máximo de mensagens reservadas por lote.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Espera do <c>OutboxPublisherService</c> quando um lote não publica nada.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Teto da espera do laço do <c>OutboxPublisherService</c> depois de lotes que falharam (banco fora, por exemplo):
    /// a espera dobra a partir de <see cref="PollInterval"/> a cada falha seguida, até aqui, e volta ao normal no
    /// primeiro lote bem-sucedido. 30 s limita o log a ~2 linhas por minuto por módulo e atrasa os eventos em no
    /// máximo 30 s depois que o banco volta.
    /// </summary>
    public TimeSpan MaxLoopBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Teto do backoff exponencial entre tentativas de uma mesma mensagem.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Por quanto tempo mensagens já publicadas ficam na tabela antes da limpeza.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Intervalo mínimo entre duas limpezas de retenção do mesmo processador (a primeira roda no primeiro lote).
    /// </summary>
    public TimeSpan RetentionCleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
