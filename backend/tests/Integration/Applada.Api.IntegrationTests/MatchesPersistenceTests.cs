using System.Diagnostics;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class MatchesPersistenceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Database_rejects_confirmed_count_above_capacity()
    {
        var matchId = await CreateMatchAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE matches.matches SET confirmed_count = capacity + 1 WHERE id = {matchId}", Ct));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_matches_confirmed_within_capacity", ex.ConstraintName);
    }

    [Fact]
    public async Task Unique_registration_per_user_and_match()
    {
        var userId = Guid.CreateVersion7();
        var matchId = await CreateMatchAsync(userIdsToRegister: [userId]);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = factory.Clock.GetUtcNow();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
            INSERT INTO matches.registrations (id, match_id, user_id, status, queue_seq, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {matchId}, {userId}, 'waitlisted', 99, {now}, {now})
            """, Ct));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("ix_registrations_match_id_user_id", ex.ConstraintName);
    }

    [Fact]
    public async Task Lock_timeout_maps_to_lock_timeout_error()
    {
        var matchId = await CreateMatchAsync();

        // Transação A, em conexão própria, segura o lock da partida até o fim do teste.
        await using var holder = new NpgsqlConnection(ConnectionString);
        await holder.OpenAsync(Ct);
        await using var holderTx = await holder.BeginTransactionAsync(Ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM matches.matches WHERE id = $1 FOR UPDATE", holder, holderTx))
        {
            lockCmd.Parameters.Add(new NpgsqlParameter { Value = matchId });
            await lockCmd.ExecuteScalarAsync(Ct);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = new MatchesUnitOfWork(
            scope.ServiceProvider.GetRequiredService<MatchesDbContext>(),
            factory.Clock,
            Options.Create(new MatchesPersistenceOptions { LockTimeout = TimeSpan.FromSeconds(1) }),
            NullLogger<MatchesUnitOfWork>.Instance);
        var reachedAfterLock = false;

        var elapsed = Stopwatch.StartNew();
        var result = await unitOfWork.ExecuteAsync<bool>(
            async (session, ct) =>
            {
                await session.GetMatchForUpdateAsync(matchId, ct);
                reachedAfterLock = true;
                return true;
            },
            Ct);
        elapsed.Stop();

        Assert.False(result.IsSuccess);
        Assert.Equal("lock-timeout", result.Error.Code);
        Assert.Equal(ErrorKind.Unavailable, result.Error.Kind);
        Assert.False(reachedAfterLock);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(7), $"Levou {elapsed.Elapsed}.");

        await holderTx.RollbackAsync(Ct);
    }

    [Fact]
    public async Task Rolled_back_work_leaves_no_outbox_message()
    {
        var aggregateId = Guid.CreateVersion7();
        Guid? matchId = null;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
            var result = await unitOfWork.ExecuteAsync<Guid>(
                (session, _) =>
                {
                    var match = NewMatch();
                    matchId = match.Id;
                    session.AddMatch(match);
                    session.Publish(new TestMatchEvent(aggregateId));
                    return Task.FromResult<Result<Guid>>(MatchErrors.MatchCancelled);
                },
                Ct);

            Assert.False(result.IsSuccess);
            Assert.Equal("match-cancelled", result.Error.Code);
        }

        Assert.Equal(0, await CountOutboxAsync(aggregateId));
        Assert.Equal(0, await CountMatchesAsync(matchId!.Value));
    }

    [Fact]
    public async Task Exception_in_work_rolls_back_and_propagates()
    {
        var aggregateId = Guid.CreateVersion7();
        Guid? matchId = null;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync<Guid>(
                (session, _) =>
                {
                    var match = NewMatch();
                    matchId = match.Id;
                    session.AddMatch(match);
                    session.Publish(new TestMatchEvent(aggregateId));
                    throw new InvalidOperationException("falha simulada");
                },
                Ct));
        }

        Assert.Equal(0, await CountOutboxAsync(aggregateId));
        Assert.Equal(0, await CountMatchesAsync(matchId!.Value));
    }

    [Fact]
    public async Task Successful_work_persists_match_and_outbox_atomically()
    {
        var registered = Guid.CreateVersion7();
        Guid matchId;
        string? lockTimeoutInsideWork = null;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
            var result = await unitOfWork.ExecuteAsync<Guid>(
                async (session, ct) =>
                {
                    lockTimeoutInsideWork = (await db.Database
                        .SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"")
                        .ToListAsync(ct)).Single();

                    var match = NewMatch();
                    session.AddMatch(match);
                    session.AddRegistration(match.Register(registered, null, factory.Clock.GetUtcNow()).Value.Registration);
                    session.Publish(new TestMatchEvent(match.Id));
                    return match.Id;
                },
                Ct);

            Assert.True(result.IsSuccess);
            matchId = result.Value;
        }

        Assert.Equal("5s", lockTimeoutInsideWork);

        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<MatchesDbContext>();

        var stored = await verifyDb.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, Ct);
        Assert.Equal(MatchStatus.Scheduled, stored.Status);
        Assert.Equal(MatchVisibility.Public, stored.Visibility);
        Assert.Equal(1, stored.ConfirmedCount);
        Assert.Equal(2, stored.NextQueueSeq);
        Assert.Equal(1, stored.Version);

        // Enums gravados como texto minúsculo (são esses os valores dos CHECKs).
        var raw = (await verifyDb.Database.SqlQuery<string>($"""
            SELECT m.status || '/' || m.visibility || '/' || r.status AS "Value"
            FROM matches.matches m JOIN matches.registrations r ON r.match_id = m.id
            WHERE m.id = {matchId}
            """).ToListAsync(Ct)).Single();
        Assert.Equal("scheduled/public/confirmed", raw);

        var outbox = await verifyDb.OutboxMessages.AsNoTracking().Where(o => o.AggregateId == matchId).ToListAsync(Ct);
        var message = Assert.Single(outbox);
        Assert.Equal(TestMatchEvent.Type, message.EventType);
        Assert.Null(message.PublishedAt);
        Assert.False(string.IsNullOrWhiteSpace(message.CorrelationId));
        // timestamptz guarda microssegundos; o relógio de teste tem ticks de 100 ns.
        Assert.True((factory.Clock.GetUtcNow() - message.OccurredAt).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Locked_match_reads_registrations_and_waitlist_head_in_queue_order()
    {
        var confirmed = Guid.CreateVersion7();
        var waitlisted = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var matchId = await CreateMatchAsync(capacity: 1, userIdsToRegister: [confirmed, .. waitlisted]);

        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var result = await unitOfWork.ExecuteAsync<bool>(
            async (session, ct) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, ct);
                Assert.NotNull(match);
                Assert.Equal(1, match.ConfirmedCount);
                Assert.Equal(3, match.WaitlistCount);

                var head = await session.GetWaitlistHeadAsync(matchId, 2, ct);
                Assert.Equal(waitlisted[..2], head.Select(r => r.UserId));
                Assert.All(head, r => Assert.Equal(RegistrationStatus.Waitlisted, r.Status));

                Assert.Empty(await session.GetWaitlistHeadAsync(matchId, 0, ct));
                Assert.Empty(await session.GetWaitlistHeadAsync(matchId, -1, ct));

                var registration = await session.FindRegistrationAsync(matchId, confirmed, ct);
                Assert.Equal(RegistrationStatus.Confirmed, registration?.Status);
                Assert.Null(await session.FindRegistrationAsync(matchId, Guid.CreateVersion7(), ct));
                Assert.Null(await session.GetMatchForUpdateAsync(Guid.CreateVersion7(), ct));
                return true;
            },
            Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_throws_conflict_and_keeps_first_entry()
    {
        var userId = Guid.CreateVersion7();
        var key = $"key-{Guid.NewGuid():N}";
        var first = new IdempotencyEntry(userId, key, "hash-1", 201, """{"id":"primeira"}""");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
            var result = await unitOfWork.ExecuteAsync<bool>(
                (session, _) =>
                {
                    session.AddIdempotency(first);
                    return Task.FromResult<Result<bool>>(true);
                },
                Ct);
            Assert.True(result.IsSuccess);
        }

        var duplicateMatchId = Guid.Empty;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
            await Assert.ThrowsAsync<IdempotencyConflictException>(() => unitOfWork.ExecuteAsync<bool>(
                (session, _) =>
                {
                    // Corrida perdida: a operação refeita não pode sobreviver ao conflito da chave.
                    var match = NewMatch();
                    duplicateMatchId = match.Id;
                    session.AddMatch(match);
                    session.AddIdempotency(first with { RequestHash = "hash-2" });
                    return Task.FromResult<Result<bool>>(true);
                },
                Ct));

            // O handler relê a resposta guardada no mesmo escopo: o contexto volta limpo e sem transação.
            var stored = await unitOfWork.ExecuteAsync<IdempotencyEntry?>(
                async (session, ct) => await session.FindIdempotencyAsync(userId, key, ct),
                Ct);
            Assert.Equal(first, stored.Value);
        }

        Assert.Equal(0, await CountMatchesAsync(duplicateMatchId));
    }

    [Fact]
    public async Task Execute_reads_the_locked_row_even_when_the_scope_tracked_an_older_copy()
    {
        var matchId = await CreateMatchAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var tracked = await db.Matches.SingleAsync(m => m.Id == matchId, Ct);
        Assert.Equal("Pelada de teste", tracked.Title);

        // Outra conexão muda a linha depois da leitura rastreada (sem lock) feita neste escopo.
        await using (var other = new NpgsqlConnection(ConnectionString))
        {
            await other.OpenAsync(Ct);
            await using var update = new NpgsqlCommand("UPDATE matches.matches SET title = $1 WHERE id = $2", other);
            update.Parameters.Add(new NpgsqlParameter { Value = "Alterado por fora" });
            update.Parameters.Add(new NpgsqlParameter { Value = matchId });
            Assert.Equal(1, await update.ExecuteNonQueryAsync(Ct));
        }

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var result = await unitOfWork.ExecuteAsync<string>(
            async (session, ct) => (await session.GetMatchForUpdateAsync(matchId, ct))!.Title,
            Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alterado por fora", result.Value);
    }

    [Fact]
    public async Task Execute_refuses_pending_changes_made_outside_the_unit_of_work()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var outside = NewMatch();
        db.Matches.Add(outside);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var reachedWork = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync<bool>(
            (_, _) =>
            {
                reachedWork = true;
                return Task.FromResult<Result<bool>>(true);
            },
            Ct));

        Assert.False(reachedWork);
        Assert.Equal(0, await CountMatchesAsync(outside.Id));
    }

    [Fact]
    public async Task Unique_violation_on_another_constraint_propagates_as_db_update_exception()
    {
        var userId = Guid.CreateVersion7();
        var matchId = await CreateMatchAsync(userIdsToRegister: [userId]);

        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteAsync<bool>(
            async (session, ct) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, ct);
                // Sem a inscrição existente, o domínio cria outra linha para o mesmo usuário: 23505 no UNIQUE(match_id, user_id).
                session.AddRegistration(match!.Register(userId, null, factory.Clock.GetUtcNow()).Value.Registration);
                return true;
            },
            Ct));

        var postgres = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ix_registrations_match_id_user_id", postgres.ConstraintName);
    }

    private string ConnectionString =>
        factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("applada")!;

    private Match NewMatch(int capacity = 10)
    {
        var now = factory.Clock.GetUtcNow();
        var data = new NewMatch("Pelada de teste", now.AddDays(2), "America/Sao_Paulo", 90, capacity, "public", null, null, null);
        return Match.Create(data, Guid.CreateVersion7(), now).Value;
    }

    private async Task<Guid> CreateMatchAsync(int capacity = 10, IReadOnlyList<Guid>? userIdsToRegister = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var result = await unitOfWork.ExecuteAsync<Guid>(
            (session, _) =>
            {
                var match = NewMatch(capacity);
                session.AddMatch(match);
                foreach (var userId in userIdsToRegister ?? [])
                {
                    session.AddRegistration(match.Register(userId, null, factory.Clock.GetUtcNow()).Value.Registration);
                }

                return Task.FromResult<Result<Guid>>(match.Id);
            },
            Ct);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<int> CountOutboxAsync(Guid aggregateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        return await db.OutboxMessages.CountAsync(o => o.AggregateId == aggregateId, Ct);
    }

    private async Task<int> CountMatchesAsync(Guid matchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        return await db.Matches.CountAsync(m => m.Id == matchId, Ct);
    }

    private sealed record TestMatchEvent(Guid MatchId) : IntegrationEvent
    {
        public const string Type = "matches.test-event.v1";

        public override string EventType => Type;

        public override int SchemaVersion => 1;

        public override Guid AggregateId => MatchId;
    }
}
