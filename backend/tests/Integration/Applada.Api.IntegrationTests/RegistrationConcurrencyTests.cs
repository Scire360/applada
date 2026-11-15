using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// Concorrência real contra o PostgreSQL: cada teste cria a própria partida e os próprios usuários e dispara as
/// requisições juntas com <see cref="Task.WhenAll(Task[])"/>, um <see cref="HttpClient"/> por usuário. Cada cenário roda 5
/// vezes. Um 503 (<c>lock-timeout</c>) nunca é aceito em silêncio: o teste falha dizendo quantas requisições estouraram.
/// </summary>
[Collection("api")]
public class RegistrationConcurrencyTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Fifty_concurrent_registrations_on_capacity_20(int run)
    {
        using var organizer = await NewUserAsync(factory, "conc");
        var players = await NewUsersAsync(factory, 50, "conc");
        try
        {
            var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 20);

            var elapsed = Stopwatch.StartNew();
            var responses = await Task.WhenAll(players.Select(async p => (p.Id, Response: await CallAsync(RegisterAsync(p.Client, matchId)))));
            elapsed.Stop();
            Report(run, "50 inscrições", elapsed);

            AssertNoLockTimeout(run, responses.Select(r => r.Response));
            Assert.All(responses, r => Assert.True(r.Response.Status == HttpStatusCode.Created, $"Rodada {run}: {r.Response.Status} {r.Response.Body}"));
            var results = responses.Select(r => (r.Id, Registration: ReadRegistration(JsonDocument.Parse(r.Response.Body).RootElement))).ToArray();
            Assert.Equal(20, results.Count(r => r.Registration == ("confirmed", null)));
            var waitlisted = results.Where(r => r.Registration.Status == "waitlisted").ToArray();
            Assert.Equal(30, waitlisted.Length);
            Assert.Equal(Enumerable.Range(1, 30), waitlisted.Select(r => r.Registration.Position!.Value).Order());

            var state = await ReadStateAsync(factory.Services, matchId);
            AssertConsistent(state, $"Rodada {run}:");
            Assert.Equal((20, 30, 50), (state.ConfirmedCount, state.WaitlistCount, state.Rows.Count));
            // FIFO pela ordem do lock: os confirmados têm as 20 menores posições de fila.
            Assert.True(state.Confirmed.Max(r => r.QueueSeq) < state.Waitlisted.Min(r => r.QueueSeq), $"Rodada {run}: confirmado depois de alguém em espera.");
            // A posição devolvida a cada um é a ordem real dele na espera.
            var rank = state.Waitlisted.Select((r, i) => (r.UserId, Position: i + 1)).ToDictionary(x => x.UserId, x => x.Position);
            Assert.All(waitlisted, r => Assert.Equal(rank[r.Id], r.Registration.Position));
            Assert.Equal(results.Where(r => r.Registration.Status == "confirmed").Select(r => r.Id).Order(), state.Confirmed.Select(r => r.UserId).Order());

            var confirmations = await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed);
            Assert.Equal(state.Confirmed.Select(r => r.UserId).Order(), confirmations.Select(p => p.GetProperty("userId").GetGuid()).Order());
            Assert.All(confirmations, p => Assert.Equal("direct", p.GetProperty("source").GetString()));
        }
        finally
        {
            DisposeAll(players);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Concurrent_withdrawals_and_registrations_never_exceed_capacity(int run)
    {
        using var organizer = await NewUserAsync(factory, "conc");
        var players = await NewUsersAsync(factory, 30, "conc");
        var newcomers = await NewUsersAsync(factory, 10, "conc");
        try
        {
            var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 20);
            // Em sequência: os 20 primeiros confirmam, os 10 seguintes esperam nessa ordem.
            foreach (var player in players)
            {
                await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
            }

            var leavers = players[..10];
            var waiting = players[20..];
            var before = await ReadStateAsync(factory.Services, matchId);
            Assert.Equal(waiting.Select(p => p.Id), before.Waitlisted.Select(r => r.UserId));

            // Relógio crescente a cada leitura: o updated_at da promoção (lido sob o lock) ordena as saídas no banco.
            (Guid Id, bool Leaving, HttpResult Response)[] responses;
            var elapsed = Stopwatch.StartNew();
            using (factory.Clock.AdvanceOnEachRead(TimeSpan.FromMilliseconds(1)))
            {
                var calls = leavers.Select(async p => (p.Id, Leaving: true, Response: await CallAsync(WithdrawAsync(p.Client, matchId))))
                    .Concat(newcomers.Select(async p => (p.Id, Leaving: false, Response: await CallAsync(RegisterAsync(p.Client, matchId)))));
                responses = await Task.WhenAll(calls);
            }

            elapsed.Stop();
            Report(run, "10 saídas + 10 inscrições", elapsed);

            AssertNoLockTimeout(run, responses.Select(r => r.Response));
            Assert.All(responses.Where(r => r.Leaving), r => Assert.True(r.Response.Status == HttpStatusCode.NoContent, $"Rodada {run}: {r.Response.Status} {r.Response.Body}"));
            // Enquanto houver espera, cada saída promove na mesma transação: quem chega nunca acha vaga livre.
            Assert.All(responses.Where(r => !r.Leaving), r =>
            {
                Assert.True(r.Response.Status == HttpStatusCode.Created, $"Rodada {run}: {r.Response.Status} {r.Response.Body}");
                Assert.Equal("waitlisted", ReadRegistration(JsonDocument.Parse(r.Response.Body).RootElement).Status);
            });

            var state = await ReadStateAsync(factory.Services, matchId);
            AssertConsistent(state, $"Rodada {run}:");
            Assert.Equal((20, 10), (state.ConfirmedCount, state.WaitlistCount));
            Assert.Equal(players[10..].Select(p => p.Id).Order(), state.Confirmed.Select(r => r.UserId).Order());
            Assert.Equal(newcomers.Select(p => p.Id).Order(), state.Waitlisted.Select(r => r.UserId).Order());

            // Promoções na ordem de queue_seq: ordenadas pelo momento da promoção, as posições de fila só crescem.
            var promoted = state.Rows.Where(r => waiting.Any(w => w.Id == r.UserId)).OrderBy(r => r.UpdatedAt).ToArray();
            Assert.Equal(10, promoted.Select(r => r.UpdatedAt).Distinct().Count());
            Assert.Equal(before.Waitlisted.Select(r => r.QueueSeq), promoted.Select(r => r.QueueSeq));

            // Cada ex-espera promovido exatamente uma vez; cada saída liberou uma vaga exatamente uma vez.
            var promotions = (await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed))
                .Where(p => p.GetProperty("source").GetString() == "waitlist_promotion")
                .Select(p => p.GetProperty("userId").GetGuid())
                .ToArray();
            Assert.Equal(waiting.Select(p => p.Id).Order(), promotions.Order());
            var releases = (await OutboxPayloadsAsync(factory.Services, matchId, SpotReleased)).Select(p => p.GetProperty("userId").GetGuid());
            Assert.Equal(leavers.Select(p => p.Id).Order(), releases.Order());
        }
        finally
        {
            DisposeAll(players);
            DisposeAll(newcomers);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Concurrent_capacity_increase_and_registrations(int run)
    {
        using var organizer = await NewUserAsync(factory, "conc");
        var players = await NewUsersAsync(factory, 20, "conc");
        var newcomers = await NewUsersAsync(factory, 5, "conc");
        try
        {
            var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 10);
            foreach (var player in players)
            {
                await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
            }

            var elapsed = Stopwatch.StartNew();
            var patch = CallAsync(PatchCapacityAsync(organizer.Client, matchId, version: 1, capacity: 15));
            var registrations = Task.WhenAll(newcomers.Select(async p => (p.Id, Response: await CallAsync(RegisterAsync(p.Client, matchId)))));
            await Task.WhenAll(patch, registrations);
            elapsed.Stop();
            Report(run, "PATCH capacidade + 5 inscrições", elapsed);

            var patched = await patch;
            var registered = await registrations;
            AssertNoLockTimeout(run, registered.Select(r => r.Response).Append(patched));
            Assert.True(patched.Status == HttpStatusCode.OK, $"Rodada {run}: {patched.Status} {patched.Body}");
            // Antes do PATCH a partida está lotada; depois dele, também (os 5 novos lugares vão para a espera).
            Assert.All(registered, r =>
            {
                Assert.True(r.Response.Status == HttpStatusCode.Created, $"Rodada {run}: {r.Response.Status} {r.Response.Body}");
                Assert.Equal("waitlisted", ReadRegistration(JsonDocument.Parse(r.Response.Body).RootElement).Status);
            });

            var state = await ReadStateAsync(factory.Services, matchId);
            AssertConsistent(state, $"Rodada {run}:");
            Assert.Equal((15, 15, 10), (state.Capacity, state.ConfirmedCount, state.WaitlistCount));
            Assert.Equal(players[..15].Select(p => p.Id).Order(), state.Confirmed.Select(r => r.UserId).Order());
            Assert.Equal(players[15..].Select(p => p.Id).Concat(newcomers.Select(p => p.Id)).Order(), state.Waitlisted.Select(r => r.UserId).Order());
            // Os que já esperavam continuam na frente de quem chegou durante o PATCH.
            Assert.Equal(players[15..].Select(p => p.Id), state.Waitlisted.Take(5).Select(r => r.UserId));

            var promotions = (await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed))
                .Where(p => p.GetProperty("source").GetString() == "waitlist_promotion")
                .Select(p => p.GetProperty("userId").GetGuid());
            Assert.Equal(players[10..15].Select(p => p.Id).Order(), promotions.Order());
        }
        finally
        {
            DisposeAll(players);
            DisposeAll(newcomers);
        }
    }

    private static async Task<HttpResult> CallAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return new HttpResult(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static void AssertNoLockTimeout(int run, IEnumerable<HttpResult> responses)
    {
        var timeouts = responses.Count(r => r.Status == HttpStatusCode.ServiceUnavailable);
        Assert.True(
            timeouts == 0,
            $"Rodada {run}: {timeouts} requisição(ões) com 503 lock-timeout. A espera pelo lock da partida passou do lock_timeout: investigar a contenção, não tolerar.");
    }

    private static void Report(int run, string scenario, Stopwatch elapsed) =>
        TestContext.Current.TestOutputHelper?.WriteLine($"Rodada {run}: {scenario} em {elapsed.ElapsedMilliseconds} ms.");

    private static void DisposeAll(IEnumerable<TestUser> users)
    {
        foreach (var user in users)
        {
            user.Dispose();
        }
    }

    private sealed record HttpResult(HttpStatusCode Status, string Body);
}
