using System.Diagnostics.Metrics;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Instrumentos do meter <c>Applada.Outbox</c>. Toda medição leva a tag <c>outbox</c> com o nome do
/// <c>DbContext</c> do módulo, para separar os outboxes de cada schema.
/// </summary>
public sealed class OutboxMetrics
{
    public const string MeterName = "Applada.Outbox";

    private const string OutboxTag = "outbox";

    private readonly Counter<long> _published;
    private readonly Counter<long> _publishFailures;
    private readonly Histogram<double> _publishLatency;
    private readonly Gauge<long> _pending;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _published = meter.CreateCounter<long>("outbox.published", description: "Mensagens publicadas e confirmadas pelo broker.");
        _publishFailures = meter.CreateCounter<long>("outbox.publish_failures", description: "Tentativas de publicação que falharam.");
        _publishLatency = meter.CreateHistogram<double>("outbox.publish_latency", unit: "ms", description: "Tempo entre occurred_at e a confirmação da publicação.");
        _pending = meter.CreateGauge<long>("outbox.pending", description: "Mensagens ainda não publicadas na última leitura.");
    }

    public void Published(string outbox, TimeSpan latency)
    {
        var tag = new KeyValuePair<string, object?>(OutboxTag, outbox);
        _published.Add(1, tag);
        _publishLatency.Record(latency.TotalMilliseconds, tag);
    }

    public void PublishFailed(string outbox) => _publishFailures.Add(1, new KeyValuePair<string, object?>(OutboxTag, outbox));

    public void Pending(string outbox, long count) => _pending.Record(count, new KeyValuePair<string, object?>(OutboxTag, outbox));
}
