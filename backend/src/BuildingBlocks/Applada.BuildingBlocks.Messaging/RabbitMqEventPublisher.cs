using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence.Outbox;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Publica mensagens do outbox no exchange topic durável <c>applada.events</c>, com routing key =
/// <c>EventType</c>, mensagens persistentes e publisher confirms rastreados pelo cliente: o
/// <see cref="PublishAsync"/> só retorna depois do <c>basic.ack</c>; nack ou timeout lançam exceção.
/// </summary>
/// <remarks>
/// <para>
/// Singleton dono de uma conexão e de um canal. Todo acesso a eles (criação, publicação, verificação de
/// saúde e descarte) passa pelo semáforo <c>_gate</c>: publicações concorrentes são serializadas no
/// canal compartilhado, o que dispensa depender da segurança de thread do canal e mantém a criação
/// preguiçosa livre de corrida. O custo é uma confirmação por vez por processo, suficiente para o
/// volume do outbox.
/// </para>
/// <para>
/// A recuperação automática do cliente fica desligada: conexão ou canal fechados são descartados e
/// recriados na próxima chamada, e a mensagem que falhou volta a ser tentada pelo outbox.
/// </para>
/// <para>Nunca registra o payload nem a connection string (que contém credenciais).</para>
/// </remarks>
public sealed partial class RabbitMqEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    public const string ExchangeName = "applada.events";

    /// <summary>Tempo máximo para o broker confirmar uma publicação.</summary>
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqEventPublisher(Uri connectionUri, ILogger<RabbitMqEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionUri);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = new ConnectionFactory
        {
            Uri = connectionUri,
            ClientProvidedName = "applada-outbox-publisher",
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            RequestedConnectionTimeout = ConnectTimeout,
        };
        _logger = logger;
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = SerializeEnvelope(message);
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Type = message.EventType,
        };

        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var channel = await EnsureChannelAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConfirmTimeout);
            try
            {
                // Com rastreamento de confirmações ligado, só retorna após o ack; nack vira PublishException.
                await channel.BasicPublishAsync(ExchangeName, message.EventType, mandatory: false, properties, body, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Sem confirmação no prazo: o estado do canal é incerto, então ele é descartado.
                await ResetChannelAsync();
                throw new TimeoutException($"O broker não confirmou a publicação em {ConfirmTimeout.TotalSeconds:0} s.");
            }
            catch (Exception ex) when (ex is not PublishException)
            {
                // Falha de conexão ou de canal: recria na próxima chamada. Nack não invalida o canal.
                await ResetChannelAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Abre a conexão se preciso e informa se ela está aberta. Usado pelo health check.
    /// </summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureChannelAsync(ct);
            return _connection is { IsOpen: true };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await ResetChannelAsync();
            await ResetConnectionAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ReadOnlyMemory<byte> SerializeEnvelope(OutboxMessage message)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        var envelope = new IntegrationEventEnvelope(
            message.Id,
            message.EventType,
            message.SchemaVersion,
            message.AggregateId,
            message.OccurredAt,
            message.CorrelationId,
            payload.RootElement);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, IntegrationEventJson.Options);
    }

    // Chamado sempre com _gate adquirido.
    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true } && _connection is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        if (_connection is not { IsOpen: true })
        {
            await ResetConnectionAsync();
            _connection = await _connectionFactory.CreateConnectionAsync(ct);
            LogConnected(_logger);
        }

        var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        try
        {
            await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }

        _channel = channel;
        return channel;
    }

    private async Task ResetChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogDisposeFailed(_logger, "canal", ex);
        }
    }

    private async Task ResetConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogDisposeFailed(_logger, "conexão", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Conexão do publicador do outbox com o RabbitMQ aberta.")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao descartar {Resource} do RabbitMQ.")]
    private static partial void LogDisposeFailed(ILogger logger, string resource, Exception exception);
}
