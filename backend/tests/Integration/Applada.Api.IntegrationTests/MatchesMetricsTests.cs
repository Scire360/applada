using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using Applada.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;
using static Applada.Api.IntegrationTests.Infrastructure.ProblemAssertions;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// Métricas do meter <c>Applada.Matches</c>, observadas com <see cref="MeterListener"/> só no meter criado pelo
/// <see cref="IMeterFactory"/> do host do teste (outros hosts do mesmo processo não interferem).
/// </summary>
[Collection("api")]
public class MatchesMetricsTests(ApiFactory factory)
{
    private const string Registrations = "matches.registrations";
    private const string LockTimeouts = "matches.lock_timeouts";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_outcomes_are_counted_by_result()
    {
        using var recorder = new MetricRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using var organizer = await NewUserAsync(factory, "metric");
        var players = await NewUsersAsync(factory, 4, "metric");
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);

        await RegisterExpectingAsync(players[0].Client, matchId, HttpStatusCode.Created); // confirmed
        await RegisterExpectingAsync(players[1].Client, matchId, HttpStatusCode.Created); // waitlisted
        await RegisterExpectingAsync(players[1].Client, matchId, HttpStatusCode.OK); // unchanged
        using (var withdraw = await WithdrawAsync(players[0].Client, matchId))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode); // withdrawn + promoted
        }

        await RegisterExpectingAsync(players[2].Client, matchId, HttpStatusCode.Created); // waitlisted
        using (var patch = await PatchCapacityAsync(organizer.Client, matchId, version: 1, capacity: 2))
        {
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode); // promoted pela capacidade
        }

        using (var cancel = await organizer.Client.PostAsync(new Uri($"{MatchesPath}/{matchId}/cancellation", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        using (var rejected = await RegisterAsync(players[3].Client, matchId))
        {
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); // rejected
        }

        Assert.Equal(1, recorder.Sum(Registrations, "confirmed"));
        Assert.Equal(2, recorder.Sum(Registrations, "waitlisted"));
        Assert.Equal(1, recorder.Sum(Registrations, "unchanged"));
        Assert.Equal(1, recorder.Sum(Registrations, "withdrawn"));
        Assert.Equal(2, recorder.Sum(Registrations, "promoted"));
        Assert.Equal(1, recorder.Sum(Registrations, "rejected"));
        Assert.Equal(8, recorder.Sum(Registrations));
        Assert.Equal(0, recorder.Sum(LockTimeouts));

        foreach (var player in players)
        {
            player.Dispose();
        }
    }

    [Fact]
    public async Task Lock_timeout_is_counted_logged_with_match_id_and_not_counted_as_registration()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Matches:LockTimeout", "00:00:01")
            .ConfigureLogging(l => l.AddProvider(logs)));
        using var recorder = new MetricRecorder(host.Services.GetRequiredService<IMeterFactory>());
        using var organizer = ClientFor(host);
        using var player = ClientFor(host);
        var matchId = await CreateMatchAsync(organizer, factory.Clock.GetUtcNow().AddDays(2), capacity: 5);

        // Outra transação segura o lock da partida enquanto a inscrição espera.
        var connectionString = host.Services.GetRequiredService<IConfiguration>().GetConnectionString("applada");
        await using var holder = new NpgsqlConnection(connectionString);
        await holder.OpenAsync(Ct);
        await using var holderTx = await holder.BeginTransactionAsync(Ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM matches.matches WHERE id = $1 FOR UPDATE", holder, holderTx))
        {
            lockCmd.Parameters.Add(new NpgsqlParameter { Value = matchId });
            await lockCmd.ExecuteScalarAsync(Ct);
        }

        using var response = await RegisterAsync(player, matchId);
        await holderTx.RollbackAsync(Ct);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "lock-timeout");
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, recorder.Sum(LockTimeouts));
        Assert.Equal(0, recorder.Sum(Registrations));
        var logged = Assert.Single(logs.Entries, e => e.Message.Contains("lock-timeout", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Contains(matchId.ToString(), logged.Message, StringComparison.Ordinal);

        // Sem o lock, a mesma inscrição passa e só então conta.
        await RegisterExpectingAsync(player, matchId, HttpStatusCode.Created);
        Assert.Equal(1, recorder.Sum(Registrations, "confirmed"));
    }

    private HttpClient ClientFor(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Issue(NewSubject("metric"), now: factory.Clock.GetUtcNow()));
        return client;
    }

    /// <summary>Soma as medições <c>long</c> do meter <c>Applada.Matches</c> criado pelo <see cref="IMeterFactory"/> dado.</summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, long Value, string? Result)> _measurements = new();

        public MetricRecorder(IMeterFactory scope)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Applada.Matches" && ReferenceEquals(instrument.Meter.Scope, scope))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                string? result = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "result")
                    {
                        result = tag.Value as string;
                    }
                }

                _measurements.Enqueue((instrument.Name, value, result));
            });
            _listener.Start();
        }

        public long Sum(string instrument, string? result = null) =>
            _measurements.Where(m => m.Instrument == instrument && (result is null || m.Result == result)).Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();
    }
}
