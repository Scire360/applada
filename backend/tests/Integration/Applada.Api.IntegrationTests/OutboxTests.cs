using System.Data.Common;
using System.Net;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Contracts;
using Applada.Modules.Identity.Infrastructure;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

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
    public async Task Matches_processor_publishes_match_created_envelope_from_matches_schema()
    {
        // Exercita o ClaimBatchSql de MatchesDbContext (schema matches), não só o do Identity.
        await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "matches.#");
        using var organizer = await MatchesApi.NewUserAsync(factory, "outbox-org");
        var matchId = await MatchesApi.CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(3), capacity: 10);
        var outbox = Assert.Single(
            await MatchesOutboxRowsForAsync(matchId),
            m => m.EventType == MatchCreatedV1.Type);
        Assert.Null(outbox.PublishedAt);

        await DrainAsync<MatchesDbContext>();

        var message = await queue.WaitForAsync(m => m.BasicProperties.MessageId == outbox.Id.ToString(), ReceiveTimeout);
        Assert.Equal("applada.events", message.Exchange);
        Assert.Equal(MatchCreatedV1.Type, message.RoutingKey);
        Assert.Equal(outbox.CorrelationId, message.BasicProperties.CorrelationId);
        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(message.Body.Span, IntegrationEventJson.Options);
        Assert.NotNull(envelope);
        Assert.Equal(outbox.Id, envelope.EventId);
        Assert.Equal(MatchCreatedV1.Type, envelope.EventType);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(matchId, envelope.AggregateId);
        Assert.Equal(matchId, envelope.Payload.GetProperty("matchId").GetGuid());
        Assert.Equal(organizer.Id, envelope.Payload.GetProperty("organizerId").GetGuid());

        var published = Assert.Single(await MatchesOutboxRowsForAsync(matchId), m => m.Id == outbox.Id);
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
    public async Task Retention_cleanup_runs_at_most_once_per_interval_per_processor()
    {
        // A limpeza varre a tabela (published_at sem índice): roda na primeira chamada e depois no máximo uma vez
        // por RetentionCleanupInterval, não a cada lote.
        var start = factory.Clock.GetUtcNow();
        var options = new OutboxOptions();
        var processor = CreateProcessor(factory.Services.GetRequiredService<IIntegrationEventPublisher>(), options);
        var expired = OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-retention-throttle", start - options.Retention - TimeSpan.FromDays(1));
        expired.MarkPublished(start - options.Retention - TimeSpan.FromSeconds(1));
        try
        {
            await processor.ProcessBatchAsync(Ct);
            await InsertOutboxAsync([expired]);

            factory.Clock.Advance(options.RetentionCleanupInterval - TimeSpan.FromSeconds(1));
            await processor.ProcessBatchAsync(Ct);
            Assert.Contains(expired.Id, await OutboxIdsAsync([expired.Id]));

            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            await processor.ProcessBatchAsync(Ct);
            Assert.DoesNotContain(expired.Id, await OutboxIdsAsync([expired.Id]));
        }
        finally
        {
            factory.Clock.SetUtcNow(start);
            await DeleteOutboxAsync(expired.Id);
        }
    }

    [Fact]
    public async Task Ready_returns_200_when_postgres_and_rabbitmq_are_up()
    {
        var checks = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(checks, c => c.Name == "postgres" && c.Tags.Contains("ready"));
        Assert.Contains(checks, c => c.Name == "rabbitmq" && c.Tags.Contains("ready") && c.FailureStatus == HealthStatus.Degraded);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_reports_degraded_when_rabbitmq_is_down()
    {
        // Com o broker fora a API continua aceitando escritas (o outbox segura os eventos): Degraded, não 503.
        // Porta 1 em loopback: conexão recusada na hora, sem depender de timeout.
        await using var brokerDown = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@127.0.0.1:1/"));
        using var client = brokerDown.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Ready_returns_503_when_postgres_is_down()
    {
        await using var databaseDown = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:applada", "Host=127.0.0.1;Port=1;Database=applada;Username=x;Password=x;Timeout=3")
            .UseSetting("Database:ApplyMigrationsOnStartup", "false"));
        using var client = databaseDown.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Connection_failure_ends_batch_and_leaves_remaining_messages_untouched()
    {
        await DrainAsync();
        var messages = await InsertOrderedAsync(3);
        var publisher = new ScriptedPublisher(_ => throw new IOException("conexão com o broker perdida (simulado)"));
        try
        {
            Assert.Equal(0, await CreateProcessor(publisher).ProcessBatchAsync(Ct));

            Assert.Equal([messages[0].Id], publisher.Calls);
            var rows = await OutboxRowsByIdAsync(messages);
            Assert.Equal(1, rows[0].Attempts);
            Assert.Contains("IOException", rows[0].LastError, StringComparison.Ordinal);
            Assert.All(rows, r => Assert.Null(r.PublishedAt));
            Assert.All(rows.Skip(1).Zip(messages.Skip(1)), pair =>
            {
                Assert.Equal(0, pair.First.Attempts);
                Assert.Null(pair.First.LastError);
                Assert.Equal(pair.Second.NextAttemptAt, pair.First.NextAttemptAt, TimeSpan.FromMilliseconds(1));
            });
        }
        finally
        {
            await DeleteOutboxAsync([.. messages.Select(m => m.Id)]);
        }
    }

    [Fact]
    public async Task Broker_nack_fails_only_that_message_and_batch_continues()
    {
        await DrainAsync();
        var messages = await InsertOrderedAsync(3);
        var publisher = new ScriptedPublisher(message =>
        {
            if (message.Id == messages[0].Id)
            {
                throw new IntegrationEventRejectedException("nack simulado");
            }
        });
        try
        {
            Assert.Equal(2, await CreateProcessor(publisher).ProcessBatchAsync(Ct));

            Assert.Equal(messages.Select(m => m.Id), publisher.Calls);
            var rows = await OutboxRowsByIdAsync(messages);
            Assert.Equal(1, rows[0].Attempts);
            Assert.Null(rows[0].PublishedAt);
            Assert.All(rows.Skip(1), r => Assert.NotNull(r.PublishedAt));
        }
        finally
        {
            await DeleteOutboxAsync([.. messages.Select(m => m.Id)]);
        }
    }

    [Fact]
    public async Task Disposing_a_test_queue_allows_the_next_identity_event_to_be_published()
    {
        var publisher = factory.Services.GetRequiredService<RabbitMqEventPublisher>();
        Assert.True(await publisher.IsConnectedAsync(Ct));

        // Repetir cobre a corrida entre a remoção da fila no broker e a publicação pelo canal já aberto.
        for (var i = 0; i < 12; i++)
        {
            var message = OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-disposal", factory.Clock.GetUtcNow());
            var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
            await queue.DisposeAsync();

            await publisher.PublishAsync(message, Ct);
        }
    }

    [Fact]
    public async Task Disposing_a_test_queue_after_the_broker_closes_its_connection_succeeds()
    {
        var publisher = factory.Services.GetRequiredService<RabbitMqEventPublisher>();
        Assert.True(await publisher.IsConnectedAsync(Ct));
        try
        {
            await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
            await factory.CloseRabbitMqConnectionsAsync();
            await WaitUntilAsync(() => !queue.IsOpen && !publisher.HasOpenConnection, TimeSpan.FromSeconds(10));
            await Task.Delay(new ConnectionFactory().NetworkRecoveryInterval + TimeSpan.FromSeconds(3), Ct);
            Assert.False(queue.IsOpen, "A fila exclusiva reabriu após o broker fechar a conexão.");
        }
        finally
        {
            await publisher.IsConnectedAsync(Ct);
        }
    }

    [Fact]
    public async Task Publisher_reconnects_after_broker_closes_its_connection()
    {
        var publisher = factory.Services.GetRequiredService<RabbitMqEventPublisher>();
        await DrainAsync();
        var before = await InsertOrderedAsync(1);
        try
        {
            Assert.Equal(1, await CreateProcessor(publisher).ProcessBatchAsync(Ct));
            Assert.True(publisher.HasOpenConnection);

            await factory.CloseRabbitMqConnectionsAsync();
            // Determinístico: espera o cliente processar o connection.close do broker. Daí em diante o
            // publicador sabe que a conexão caiu e reconecta antes de publicar, então a primeira tentativa
            // já entrega. (Se publicasse antes de perceber, a tentativa falharia, seria registrada e o lote
            // terminaria; o retry entregaria depois — caminho coberto pelo teste do broker congelado.)
            await WaitUntilAsync(() => !publisher.HasOpenConnection, TimeSpan.FromSeconds(10));
            await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
            var after = await InsertOrderedAsync(1);
            try
            {
                Assert.Equal(1, await CreateProcessor(publisher).ProcessBatchAsync(Ct));

                Assert.True(publisher.HasOpenConnection);
                await queue.WaitForAsync(m => m.BasicProperties.MessageId == after[0].Id.ToString(), ReceiveTimeout);
                var row = Assert.Single(await OutboxRowsByIdAsync(after));
                Assert.NotNull(row.PublishedAt);
                Assert.Equal(0, row.Attempts);
            }
            finally
            {
                await DeleteOutboxAsync(after[0].Id);
            }
        }
        finally
        {
            await DeleteOutboxAsync(before[0].Id);
        }
    }

    [Fact]
    public async Task Unresponsive_broker_ends_batch_within_bounded_time_and_retries_after_recovery()
    {
        var publisher = factory.Services.GetRequiredService<RabbitMqEventPublisher>();
        await DrainAsync();
        Assert.True(await publisher.IsConnectedAsync(Ct));
        var start = factory.Clock.GetUtcNow();
        var messages = await InsertOrderedAsync(3);
        var processor = CreateProcessor(publisher);
        try
        {
            await factory.PauseRabbitMqAsync();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Assert.Equal(0, await processor.ProcessBatchAsync(Ct));
            }
            finally
            {
                elapsed.Stop();
                await factory.UnpauseRabbitMqAsync();
            }

            // Medido ~20 s: confirmação (10 s) + descarte da conexão (abort 5 s + canal 0 até 5 s). Antes da
            // correção, 3 mensagens levavam ~70 s (cada uma repetia a espera); com 100 seriam vários minutos.
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), $"Lote levou {elapsed.Elapsed}.");
            var rows = await OutboxRowsByIdAsync(messages);
            Assert.Equal(1, rows[0].Attempts);
            Assert.Contains("TimeoutException", rows[0].LastError, StringComparison.Ordinal);
            Assert.All(rows.Skip(1), r => Assert.Equal(0, r.Attempts));
            Assert.All(rows, r => Assert.Null(r.PublishedAt));

            await using var queue = await TestQueue.CreateAsync(factory.RabbitMqConnectionString, "identity.#");
            factory.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(3, await processor.ProcessBatchAsync(Ct));

            var ids = messages.Select(m => m.Id.ToString()).ToHashSet();
            var received = await queue.CollectAsync(m => ids.Contains(m.BasicProperties.MessageId!), expected: 3, ReceiveTimeout);
            Assert.Equal(ids, received.Select(m => m.BasicProperties.MessageId!).ToHashSet());
            Assert.All(await OutboxRowsByIdAsync(messages), r => Assert.NotNull(r.PublishedAt));
        }
        finally
        {
            factory.Clock.SetUtcNow(start);
            await DeleteOutboxAsync([.. messages.Select(m => m.Id)]);
        }
    }

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private OutboxProcessor<IdentityDbContext> CreateProcessor(IIntegrationEventPublisher publisher, OutboxOptions? options = null) =>
        CreateProcessor<IdentityDbContext>(publisher, options);

    private OutboxProcessor<TContext> CreateProcessor<TContext>(IIntegrationEventPublisher publisher, OutboxOptions? options = null)
        where TContext : DbContext, IHasOutbox =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            factory.Clock,
            Options.Create(options ?? new OutboxOptions()),
            factory.Services.GetRequiredService<OutboxMetrics>(),
            new TestOutputLogger<OutboxProcessor<TContext>>());

    /// <summary>
    /// Escreve avisos e erros do processador na saída do teste (o xUnit mostra junto da falha). Falha intermitente
    /// de publicação, sem isso, só deixa "esperado 1, obtido 0" e nenhuma pista da causa.
    /// </summary>
    private sealed class TestOutputLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"[{logLevel}] {formatter(state, exception)} {exception?.GetType().Name}: {exception?.Message}");
            }
        }
    }

    /// <summary>
    /// Publica tudo o que está pendente (inclusive sobras de outras classes de teste da coleção).
    /// Logo depois de um teste que derruba a conexão do broker, a primeira publicação falha por desenho (o publicador
    /// só percebe a queda ao tentar) e a mensagem ganha backoff; sem nova rodada, essa sobra furaria a fila do teste
    /// seguinte. Por isso, enquanto restar mensagem que falhou, antecipa a próxima tentativa e drena de novo.
    /// </summary>
    private Task DrainAsync() => DrainAsync<IdentityDbContext>();

    private async Task DrainAsync<TContext>()
        where TContext : DbContext, IHasOutbox
    {
        var processor = CreateProcessor<TContext>(factory.Services.GetRequiredService<IIntegrationEventPublisher>());
        for (var round = 0; round < 3; round++)
        {
            while (await processor.ProcessBatchAsync(Ct) > 0)
            {
            }

            var now = factory.Clock.GetUtcNow();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var postponed = await db.OutboxMessages.Where(m => m.PublishedAt == null && m.NextAttemptAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.NextAttemptAt, now), Ct);
            if (postponed == 0)
            {
                return;
            }
        }
    }

    /// <summary>Insere mensagens pendentes com occurred_at crescente, para a ordem de reserva ser conhecida.</summary>
    private async Task<List<OutboxMessage>> InsertOrderedAsync(int count)
    {
        var now = factory.Clock.GetUtcNow();
        var messages = Enumerable.Range(0, count)
            .Select(i => OutboxMessage.From(new UserRegisteredV1(Guid.CreateVersion7()), "test-ordered", now - TimeSpan.FromSeconds(count - i)))
            .ToList();
        await InsertOutboxAsync(messages);
        return messages;
    }

    /// <summary>Linhas atuais das mensagens, na mesma ordem da lista.</summary>
    private async Task<List<OutboxMessage>> OutboxRowsByIdAsync(List<OutboxMessage> messages)
    {
        var ids = messages.Select(m => m.Id).ToList();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, Ct);
        return [.. ids.Select(id => rows[id])];
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "Condição não atingida no prazo.");
            await Task.Delay(50, Ct);
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

    private async Task<List<OutboxMessage>> MatchesOutboxRowsForAsync(Guid aggregateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
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

    /// <summary>Publisher de teste: registra cada chamada e executa a ação dada (que pode lançar).</summary>
    private sealed class ScriptedPublisher(Action<OutboxMessage> onPublish) : IIntegrationEventPublisher
    {
        private readonly List<Guid> _calls = [];
        private readonly Lock _gate = new();

        public List<Guid> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            lock (_gate)
            {
                _calls.Add(message.Id);
            }

            onPublish(message);
            return Task.CompletedTask;
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

        public bool IsOpen => _channel.IsOpen;

        public static async Task<TestQueue> CreateAsync(string connectionString, string bindingKey)
        {
            var connection = await new ConnectionFactory
            {
                Uri = new Uri(connectionString),
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
            }.CreateConnectionAsync(Ct);
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
            await using var connection = _connection;
            await using (var channel = _channel)
            {
                // Aguardamos queue.delete-ok para que o binding seja removido antes de fechar a conexão.
                // Se o canal já fechou ou fechar durante o delete, só o broker pode remover a fila exclusiva.
                // Apenas nesse caso a remoção pode coincidir com um confirm pendente e gerar um nack.
                if (channel.IsOpen)
                {
                    try
                    {
                        await channel.QueueDeleteAsync(_queue, cancellationToken: CancellationToken.None);
                    }
                    catch (Exception ex) when ((ex is AlreadyClosedException or OperationInterruptedException) && !channel.IsOpen)
                    {
                    }
                }
            }
        }
    }
}
