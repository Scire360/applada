using System.Data.Common;
using System.Net;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Contracts;
using Applada.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class OutboxTests(ApiFactory factory)
{
    private const string MePath = "/api/v1/me";
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_login_writes_user_registered_to_outbox_in_same_transaction()
    {
        var subject = NewSubject();

        var userId = await GetMeAsync(subject);

        var rows = await OutboxRowsForAsync(userId);
        var row = Assert.Single(rows);
        Assert.Equal(UserRegisteredV1.Type, row.EventType);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Equal(userId, row.AggregateId);
        Assert.False(string.IsNullOrWhiteSpace(row.CorrelationId));
        Assert.Null(row.PublishedAt);
        Assert.Equal(0, row.Attempts);
        using (var payload = JsonDocument.Parse(row.Payload))
        {
            Assert.Equal(userId, Assert.Single(payload.RootElement.EnumerateObject(), p => p.Name == "userId").Value.GetGuid());
            Assert.Single(payload.RootElement.EnumerateObject());
        }

        Assert.Equal(userId, await GetMeAsync(subject));
        Assert.Single(await OutboxRowsForAsync(userId));
    }

    [Fact]
    public async Task Failed_outbox_write_rolls_back_user_creation()
    {
        // Spec §14: rollback não deixa evento, e evento que não grava não deixa usuário.
        await using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<IdentityDbContext>(options => options.AddInterceptors(new FailOutboxInsertInterceptor()))));
        var subject = NewSubject();
        using var client = failing.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", TestJwt.Issue(subject, now: factory.Clock.GetUtcNow()));

        var response = await client.GetAsync(MePath, Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Subject == subject, Ct));
    }

    [Fact]
    public async Task Processor_publishes_envelope_to_rabbitmq()
    {
        await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
        var userId = await GetMeAsync(NewSubject());
        var outbox = Assert.Single(await OutboxRowsForAsync(userId));

        await DrainAsync();

        var message = await queue.WaitForAsync(m => m.BasicProperties.MessageId == outbox.Id.ToString(), ReceiveTimeout);
        Assert.Equal("applada.events", message.Exchange);
        Assert.Equal(UserRegisteredV1.Type, message.RoutingKey);
        Assert.Equal(DeliveryModes.Persistent, message.BasicProperties.DeliveryMode);
        Assert.Equal("application/json", message.BasicProperties.ContentType);
        Assert.Equal(UserRegisteredV1.Type, message.BasicProperties.Type);
        Assert.Equal(outbox.CorrelationId, message.BasicProperties.CorrelationId);

        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(message.Body.Span, IntegrationEventJson.Options);
        Assert.NotNull(envelope);
        Assert.Equal(outbox.Id, envelope.EventId);
        Assert.Equal(UserRegisteredV1.Type, envelope.EventType);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(userId, envelope.AggregateId);
        Assert.Equal(outbox.CorrelationId, envelope.CorrelationId);
        Assert.Equal(userId, envelope.Payload.GetProperty("userId").GetGuid());
        // Metadados vão só no envelope: o payload do evento tem apenas os dados do evento.
        Assert.Equal("userId", Assert.Single(envelope.Payload.EnumerateObject()).Name);

        var published = Assert.Single(await OutboxRowsForAsync(userId));
        Assert.NotNull(published.PublishedAt);
    }

    [Fact]
    public async Task Publish_failure_keeps_message_and_schedules_retry()
    {
        await DrainAsync();
        var start = factory.Clock.GetUtcNow();
        var userId = await GetMeAsync(NewSubject());
        var messageId = Assert.Single(await OutboxRowsForAsync(userId)).Id;
        var failing = new ThrowingPublisher();
        var processor = CreateProcessor(failing);
        try
        {
            Assert.Equal(0, await processor.ProcessBatchAsync(Ct));

            var failed = Assert.Single(await OutboxRowsForAsync(userId));
            Assert.Null(failed.PublishedAt);
            Assert.Equal(1, failed.Attempts);
            Assert.Equal(start + TimeSpan.FromSeconds(2), failed.NextAttemptAt, TimeSpan.FromMilliseconds(1));
            Assert.NotNull(failed.LastError);
            Assert.Contains(ThrowingPublisher.ErrorMessage, failed.LastError, StringComparison.Ordinal);
            Assert.DoesNotContain(userId.ToString(), failed.LastError, StringComparison.OrdinalIgnoreCase);

            // Antes de next_attempt_at a mensagem não é tentada de novo.
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            await processor.ProcessBatchAsync(Ct);
            Assert.Equal(1, failing.Attempts(messageId));
            Assert.Equal(1, Assert.Single(await OutboxRowsForAsync(userId)).Attempts);

            // Vencido o prazo, tenta de novo e dobra o backoff: now + 2^2 s.
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            await processor.ProcessBatchAsync(Ct);
            var retried = Assert.Single(await OutboxRowsForAsync(userId));
            Assert.Equal(2, failing.Attempts(messageId));
            Assert.Equal(2, retried.Attempts);
            Assert.Null(retried.PublishedAt);
            Assert.Equal(start + TimeSpan.FromSeconds(2 + 4), retried.NextAttemptAt, TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            factory.Clock.SetUtcNow(start);
            await DeleteOutboxAsync(messageId);
        }
    }

    [Fact]
    public async Task Concurrent_processors_do_not_double_publish()
    {
        await DrainAsync();
        await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
        var now = factory.Clock.GetUtcNow();
        var messages = Enumerable.Range(0, 30)
            .Select(_ => OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-concurrency", now))
            .ToList();
        await InsertOutboxAsync(messages);
        var ids = messages.Select(m => m.Id.ToString()).ToHashSet();
        // Lote de 20 < 30: nenhum processador leva tudo sozinho, então os dois disputam as linhas.
        var options = new OutboxOptions { BatchSize = 20 };
        var publisher = factory.Services.GetRequiredService<IIntegrationEventPublisher>();
        var first = CreateProcessor(publisher, options);
        var second = CreateProcessor(publisher, options);

        var results = await Task.WhenAll(
            Task.Run(() => first.ProcessBatchAsync(Ct), Ct),
            Task.Run(() => second.ProcessBatchAsync(Ct), Ct));

        Assert.Equal(30, results.Sum());
        var received = await queue.CollectAsync(m => ids.Contains(m.BasicProperties.MessageId!), expected: 30, ReceiveTimeout);
        Assert.Equal(30, received.Count);
        Assert.Equal(ids, received.Select(m => m.BasicProperties.MessageId!).ToHashSet());
    }

    [Fact]
    public async Task Published_messages_older_than_retention_are_deleted()
    {
        var now = factory.Clock.GetUtcNow();
        var retention = new OutboxOptions().Retention;
        var expired = OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-retention", now - retention - TimeSpan.FromDays(1));
        expired.MarkPublished(now - retention - TimeSpan.FromSeconds(1));
        var recent = OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-retention", now - TimeSpan.FromDays(1));
        recent.MarkPublished(now - TimeSpan.FromDays(1));
        // Pendente antiga (com retry agendado no futuro) nunca é apagada, por mais velha que seja.
        var pending = OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-retention", now - retention - TimeSpan.FromDays(1));
        pending.RecordFailure("falha de teste", now + TimeSpan.FromHours(1));
        await InsertOutboxAsync([expired, recent, pending]);
        try
        {
            await CreateProcessor(factory.Services.GetRequiredService<IIntegrationEventPublisher>()).ProcessBatchAsync(Ct);

            var remaining = await OutboxIdsAsync([expired.Id, recent.Id, pending.Id]);
            Assert.DoesNotContain(expired.Id, remaining);
            Assert.Contains(recent.Id, remaining);
            Assert.Contains(pending.Id, remaining);
        }
        finally
        {
            await DeleteOutboxAsync(recent.Id, pending.Id);
        }
    }

    [Fact]
    public async Task Ready_returns_200_when_postgres_and_rabbitmq_are_up()
    {
        var checks = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(checks, c => c.Name == "postgres" && c.Tags.Contains("ready"));
        Assert.Contains(checks, c => c.Name == "rabbitmq" && c.Tags.Contains("ready"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_rabbitmq_is_down()
    {
        // Porta 1 em loopback: conexão recusada na hora, sem depender de timeout.
        await using var brokerDown = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@127.0.0.1:1/"));
        using var client = brokerDown.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private OutboxProcessor<IdentityDbContext> CreateProcessor(IIntegrationEventPublisher publisher, OutboxOptions? options = null) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            factory.Clock,
            Options.Create(options ?? new OutboxOptions()),
            factory.Services.GetRequiredService<OutboxMetrics>(),
            NullLogger<OutboxProcessor<IdentityDbContext>>.Instance);

    /// <summary>Publica tudo o que está pendente e vencido (inclusive sobras de outras classes de teste da coleção).</summary>
    private async Task DrainAsync()
    {
        var processor = CreateProcessor(factory.Services.GetRequiredService<IIntegrationEventPublisher>());
        while (await processor.ProcessBatchAsync(Ct) > 0)
        {
        }
    }

    private async Task<Guid> GetMeAsync(string subject)
    {
        using var client = factory.CreateClientFor(subject);
        var response = await client.GetAsync(MePath, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("userId").GetGuid();
    }

    private async Task<List<OutboxMessage>> OutboxRowsForAsync(Guid aggregateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == aggregateId).ToListAsync(Ct);
    }

    private async Task<List<Guid>> OutboxIdsAsync(Guid[] ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.OutboxMessages.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(Ct);
    }

    private async Task InsertOutboxAsync(IEnumerable<OutboxMessage> messages)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.OutboxMessages.AddRange(messages);
        await db.SaveChangesAsync(Ct);
    }

    private async Task DeleteOutboxAsync(params Guid[] ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.OutboxMessages.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(Ct);
    }

    private sealed class ThrowingPublisher : IIntegrationEventPublisher
    {
        public const string ErrorMessage = "broker indisponível (simulado)";

        private readonly Dictionary<Guid, int> _attempts = [];
        private readonly Lock _gate = new();

        public int Attempts(Guid messageId)
        {
            lock (_gate)
            {
                return _attempts.GetValueOrDefault(messageId);
            }
        }

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            lock (_gate)
            {
                _attempts[message.Id] = _attempts.GetValueOrDefault(message.Id) + 1;
            }

            throw new InvalidOperationException(ErrorMessage);
        }
    }

    /// <summary>Faz falhar o INSERT no outbox para provar que o usuário criado antes dele é desfeito.</summary>
    private sealed class FailOutboxInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowIfOutboxInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ThrowIfOutboxInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void ThrowIfOutboxInsert(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("outbox_messages", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("falha simulada ao gravar o outbox");
            }
        }
    }

    /// <summary>Fila exclusiva ligada a <c>applada.events</c>, lida com basic.get.</summary>
    private sealed class TestQueue : IAsyncDisposable
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private readonly string _queue;

        private TestQueue(IConnection connection, IChannel channel, string queue)
        {
            _connection = connection;
            _channel = channel;
            _queue = queue;
        }

        public static async Task<TestQueue> CreateAsync(string connectionString, string bindingKey)
        {
            var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync(Ct);
            var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
            await channel.ExchangeDeclareAsync("applada.events", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: Ct);
            var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: Ct);
            await channel.QueueBindAsync(queue.QueueName, "applada.events", bindingKey, cancellationToken: Ct);
            return new TestQueue(connection, channel, queue.QueueName);
        }

        public async Task<BasicGetResult> WaitForAsync(Func<BasicGetResult, bool> match, TimeSpan timeout)
        {
            var received = await CollectAsync(match, expected: 1, timeout);
            return Assert.Single(received);
        }

        /// <summary>
        /// Lê até <paramref name="expected"/> mensagens que casam e, depois disso, espera mais um pouco
        /// para que uma eventual duplicata também apareça na contagem.
        /// </summary>
        public async Task<List<BasicGetResult>> CollectAsync(Func<BasicGetResult, bool> match, int expected, TimeSpan timeout)
        {
            var received = new List<BasicGetResult>();
            var deadline = DateTimeOffset.UtcNow + timeout;
            DateTimeOffset? settleUntil = null;

            while (DateTimeOffset.UtcNow < (settleUntil ?? deadline))
            {
                var message = await _channel.BasicGetAsync(_queue, autoAck: true, Ct);
                if (message is null)
                {
                    await Task.Delay(50, Ct);
                    continue;
                }

                if (match(message))
                {
                    received.Add(message);
                    if (received.Count == expected)
                    {
                        settleUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500);
                    }
                }
            }

            return received;
        }

        public async ValueTask DisposeAsync()
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
    }
}
