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
/// <see cref="PublishAsync"/> só retorna depois do <c>basic.ack</c>. Nack vira
/// <see cref="IntegrationEventRejectedException"/> (problema só desta mensagem); falha de conexão, de canal
/// ou falta de confirmação em <see cref="ConfirmTimeout"/> propagam outra exceção (problema do broker) e
/// descartam conexão e canal.
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
/// <para>
/// Pior caso de uma chamada com o broker congelado (rede em buraco negro), medido em teste em ~20 s:
/// confirmação (10 s) + descarte da conexão, que é o abort (prazo fixo de 5 s no cliente) mais o fechamento
/// do canal 0 (até <c>ContinuationTimeout</c>, 5 s). Na abertura, cada etapa tem prazo de 5 s (ver
/// <c>OperationTimeout</c>).
/// </para>
/// <para>Nunca registra o payload nem a connection string (que contém credenciais).</para>
/// </remarks>
public sealed partial class RabbitMqEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    public const string ExchangeName = "applada.events";

    /// <summary>Tempo máximo para o broker confirmar uma publicação.</summary>
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Prazo de cada etapa de abertura: TCP (<c>RequestedConnectionTimeout</c>), handshake AMQP
    /// (<c>HandshakeContinuationTimeout</c>, padrão do cliente 10 s) e RPCs de canal como abrir, declarar o
    /// exchange e fechar (<c>ContinuationTimeout</c>, padrão do cliente 20 s). Conferido no código do
    /// RabbitMQ.Client 7.2.2: sem baixar os padrões, um broker congelado prenderia o lote por até 20 s por RPC.
    /// </summary>
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

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
            RequestedConnectionTimeout = OperationTimeout,
            HandshakeContinuationTimeout = OperationTimeout,
            ContinuationTimeout = OperationTimeout,
        };
        _logger = logger;
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = IntegrationEventEnvelopeSerializer.Serialize(message);
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
            try
            {
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
                    throw new TimeoutException($"O broker não confirmou a publicação em {ConfirmTimeout.TotalSeconds:0} s.");
                }
            }
            catch (PublishException ex)
            {
                // Nack: o broker recusou só esta mensagem; conexão e canal continuam bons.
                throw new IntegrationEventRejectedException("O broker recusou (nack) a publicação.", ex);
            }
            catch (Exception)
            {
                // Conexão, canal ou timeout: o estado é incerto (o broker pode estar congelado), então
                // descarta tudo agora e reconecta do zero na próxima chamada.
                await ResetAsync();
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
            try
            {
                await EnsureChannelAsync(ct);
            }
            catch (Exception)
            {
                await ResetAsync();
                throw;
            }

            return _connection is { IsOpen: true };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Se a conexão atual está aberta, sem tentar abrir nem esperar o semáforo (leitura instantânea).
    /// Usado pelos testes para saber quando o cliente já percebeu um fechamento vindo do broker.
    /// </summary>
    internal bool HasOpenConnection => Volatile.Read(ref _connection) is { IsOpen: true };

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
            await ResetAsync();
        }
        finally
        {
            _gate.Release();
        }
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

        // Atribuído antes de declarar o exchange: se a declaração falhar, quem chamou descarta conexão e
        // canal juntos (ResetAsync), sem esperar o close-ok do canal.
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        return _channel;
    }

    /// <summary>
    /// Descarta conexão e canal. A conexão vai primeiro: o abort dela tem prazo fixo de 5 s no cliente e
    /// fecha os canais junto, então o descarte do canal em seguida não espera um <c>channel.close-ok</c>
    /// que um broker congelado nunca mandaria (esperaria o <c>ContinuationTimeout</c>).
    /// </summary>
    private async Task ResetAsync()
    {
        await ResetConnectionAsync();
        await ResetChannelAsync();
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
