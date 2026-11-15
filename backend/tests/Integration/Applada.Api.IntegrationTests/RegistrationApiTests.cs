using System.Net;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Domain;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// <c>POST /matches/{id}/registrations</c> e <c>DELETE /matches/{id}/registrations/me</c>. Chamada de
/// <c>RegistrationApiTests</c> (no brief, <c>RegistrationTests</c>) para não colidir no filtro por classe com os testes
/// de domínio <c>Applada.Modules.Matches.Domain.Tests.RegistrationTests</c>.
/// </summary>
[Collection("api")]
public class RegistrationApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_returns_201_confirmed_then_200_on_repeat()
    {
        using var organizer = await NewUserAsync(factory);
        using var player = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 2);

        Assert.Equal(("confirmed", (int?)null), await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created));
        Assert.Equal(("confirmed", (int?)null), await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.OK));

        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        var row = Assert.Single(state.Rows);
        Assert.Equal((player.Id, RegistrationStatus.Confirmed), (row.UserId, row.Status));
        // A repetição não grava evento de novo.
        var confirmed = Assert.Single(await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed));
        Assert.Equal(player.Id, confirmed.GetProperty("userId").GetGuid());
        Assert.Equal("direct", confirmed.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Register_on_full_match_returns_waitlisted_with_position()
    {
        using var organizer = await NewUserAsync(factory);
        using var first = await NewUserAsync(factory);
        using var second = await NewUserAsync(factory);
        using var third = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);

        Assert.Equal(("confirmed", (int?)null), await RegisterExpectingAsync(first.Client, matchId, HttpStatusCode.Created));
        Assert.Equal(("waitlisted", (int?)1), await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.Created));
        Assert.Equal(("waitlisted", (int?)2), await RegisterExpectingAsync(third.Client, matchId, HttpStatusCode.Created));
        // Repetir devolve o estado atual, com a posição calculada de novo.
        Assert.Equal(("waitlisted", (int?)2), await RegisterExpectingAsync(third.Client, matchId, HttpStatusCode.OK));

        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        Assert.Equal((1, 2), (state.ConfirmedCount, state.WaitlistCount));
        // Só a confirmação direta do primeiro; entrar na espera não gera evento.
        Assert.Single(await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed));
    }

    [Fact]
    public async Task Withdraw_confirmed_promotes_and_promoted_user_sees_confirmed()
    {
        using var organizer = await NewUserAsync(factory);
        using var first = await NewUserAsync(factory);
        using var second = await NewUserAsync(factory);
        using var third = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);
        await RegisterExpectingAsync(first.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(third.Client, matchId, HttpStatusCode.Created);

        using var withdrawn = await WithdrawAsync(first.Client, matchId);

        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        Assert.Empty(await withdrawn.Content.ReadAsByteArrayAsync(Ct));
        Assert.Null(await GetMyRegistrationAsync(first.Client, matchId));
        Assert.Equal(("confirmed", (int?)null), await GetMyRegistrationAsync(second.Client, matchId));
        Assert.Equal(("confirmed", (int?)null), await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.OK));
        Assert.Equal(("waitlisted", (int?)1), await GetMyRegistrationAsync(third.Client, matchId));

        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        Assert.Equal((1, 1), (state.ConfirmedCount, state.WaitlistCount));
        Assert.Equal(RegistrationStatus.Withdrawn, state.Rows.Single(r => r.UserId == first.Id).Status);
    }

    [Fact]
    public async Task Withdraw_waitlisted_leaves_without_promoting()
    {
        using var organizer = await NewUserAsync(factory);
        using var first = await NewUserAsync(factory);
        using var second = await NewUserAsync(factory);
        using var third = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);
        await RegisterExpectingAsync(first.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(third.Client, matchId, HttpStatusCode.Created);

        using var withdrawn = await WithdrawAsync(second.Client, matchId);

        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        Assert.Equal(("confirmed", (int?)null), await GetMyRegistrationAsync(first.Client, matchId));
        Assert.Equal(("waitlisted", (int?)1), await GetMyRegistrationAsync(third.Client, matchId));
        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        Assert.Equal((1, 1), (state.ConfirmedCount, state.WaitlistCount));
        Assert.Empty(await OutboxPayloadsAsync(factory.Services, matchId, SpotReleased));
    }

    [Fact]
    public async Task Withdraw_without_registration_returns_404()
    {
        using var organizer = await NewUserAsync(factory);
        using var player = await NewUserAsync(factory);
        using var other = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 3);
        await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);

        // Quem não está inscrito não consegue tirar ninguém: a rota só age sobre o próprio usuário.
        using (var response = await WithdrawAsync(other.Client, matchId))
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "registration-not-found");
        }

        Assert.Equal(("confirmed", (int?)null), await GetMyRegistrationAsync(player.Client, matchId));

        // Depois de sair, sair de novo também é 404.
        using (var first = await WithdrawAsync(player.Client, matchId))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var again = await WithdrawAsync(player.Client, matchId))
        {
            await AssertProblemAsync(again, HttpStatusCode.NotFound, "registration-not-found");
        }

        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        Assert.Equal((0, 0), (state.ConfirmedCount, state.WaitlistCount));
    }

    /// <summary>
    /// A ordem das regras da saída é a do domínio também sem linha nenhuma: cancelada (409) → fechada (409) → sem
    /// inscrição (404). Quem nunca se inscreveu recebe o mesmo que quem já saiu.
    /// </summary>
    [Fact]
    public async Task Withdraw_without_registration_follows_domain_order()
    {
        using var organizer = await NewUserAsync(factory);
        using var withdrawnBefore = await NewUserAsync(factory);
        var neverSubject = NewSubject("reg");
        var now = factory.Clock.GetUtcNow();
        var open = await CreateMatchAsync(organizer.Client, now.AddDays(2), capacity: 3);
        var cancelled = await CreateMatchAsync(organizer.Client, now.AddDays(2), capacity: 3);
        var closing = await CreateMatchAsync(organizer.Client, now.AddMinutes(10), capacity: 3);
        await RegisterExpectingAsync(withdrawnBefore.Client, cancelled, HttpStatusCode.Created);
        using (var left = await WithdrawAsync(withdrawnBefore.Client, cancelled))
        {
            Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        }

        using (var cancel = await organizer.Client.PostAsync(new Uri($"{MatchesPath}/{cancelled}/cancellation", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        using (var never = factory.CreateClientFor(neverSubject))
        {
            using var onOpen = await WithdrawAsync(never, open);
            await AssertProblemAsync(onOpen, HttpStatusCode.NotFound, "registration-not-found");
            using var onCancelled = await WithdrawAsync(never, cancelled);
            await AssertProblemAsync(onCancelled, HttpStatusCode.Conflict, "match-cancelled");
        }

        using (var again = await WithdrawAsync(withdrawnBefore.Client, cancelled))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, "match-cancelled");
        }

        var delta = TimeSpan.FromMinutes(10);
        factory.Clock.Advance(delta);
        try
        {
            using var never = factory.CreateClientFor(neverSubject);
            using var onClosed = await WithdrawAsync(never, closing);
            await AssertProblemAsync(onClosed, HttpStatusCode.Conflict, "registration-closed");
        }
        finally
        {
            factory.Clock.Advance(-delta);
        }
    }

    [Fact]
    public async Task Register_after_starts_at_returns_409()
    {
        var organizerSubject = NewSubject("reg");
        var playerSubject = NewSubject("reg");
        var latecomerSubject = NewSubject("reg");
        Guid matchId;
        using (var organizer = factory.CreateClientFor(organizerSubject))
        using (var player = factory.CreateClientFor(playerSubject))
        {
            matchId = await CreateMatchAsync(organizer, factory.Clock.GetUtcNow().AddMinutes(10), capacity: 5);
            await RegisterExpectingAsync(player, matchId, HttpStatusCode.Created);
        }

        // Exatamente no startsAt já está fechado (now >= startsAt). Clientes novos: o JWT acompanha o relógio.
        var delta = TimeSpan.FromMinutes(10);
        factory.Clock.Advance(delta);
        try
        {
            using var player = factory.CreateClientFor(playerSubject);
            using var latecomer = factory.CreateClientFor(latecomerSubject);

            using var register = await RegisterAsync(latecomer, matchId);
            await AssertProblemAsync(register, HttpStatusCode.Conflict, "registration-closed");
            using var withdraw = await WithdrawAsync(player, matchId);
            await AssertProblemAsync(withdraw, HttpStatusCode.Conflict, "registration-closed");
        }
        finally
        {
            factory.Clock.Advance(-delta);
        }

        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        Assert.Equal(RegistrationStatus.Confirmed, Assert.Single(state.Rows).Status);
    }

    /// <summary>Também cobre <c>Registration_after_cancel_returns_409_match_cancelled</c>, movido da Tarefa 10.</summary>
    [Fact]
    public async Task Register_on_cancelled_match_returns_409_match_cancelled()
    {
        using var organizer = await NewUserAsync(factory);
        using var player = await NewUserAsync(factory);
        using var latecomer = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 5);
        await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
        using (var cancel = await organizer.Client.PostAsync(new Uri($"{MatchesPath}/{matchId}/cancellation", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        using var register = await RegisterAsync(latecomer.Client, matchId);
        await AssertProblemAsync(register, HttpStatusCode.Conflict, "match-cancelled");
        using var repeat = await RegisterAsync(player.Client, matchId);
        await AssertProblemAsync(repeat, HttpStatusCode.Conflict, "match-cancelled");
        using var withdraw = await WithdrawAsync(player.Client, matchId);
        await AssertProblemAsync(withdraw, HttpStatusCode.Conflict, "match-cancelled");

        // Inscrições ficam como histórico.
        var state = await ReadStateAsync(factory.Services, matchId);
        AssertConsistent(state);
        var row = Assert.Single(state.Rows);
        Assert.Equal((player.Id, RegistrationStatus.Confirmed), (row.UserId, row.Status));
    }

    [Fact]
    public async Task Unknown_match_returns_404()
    {
        using var player = await NewUserAsync(factory);
        var unknown = Guid.CreateVersion7();

        using var register = await RegisterAsync(player.Client, unknown);
        await AssertProblemAsync(register, HttpStatusCode.NotFound, "match-not-found");
        using var withdraw = await WithdrawAsync(player.Client, unknown);
        await AssertProblemAsync(withdraw, HttpStatusCode.NotFound, "match-not-found");
        using var participants = await player.Client.GetAsync(new Uri($"{MatchesPath}/{unknown}/registrations", UriKind.Relative), Ct);
        await AssertProblemAsync(participants, HttpStatusCode.NotFound, "match-not-found");
    }

    [Fact]
    public async Task Withdraw_writes_spot_released_and_spot_confirmed_promotion_to_outbox()
    {
        using var organizer = await NewUserAsync(factory);
        using var first = await NewUserAsync(factory);
        using var second = await NewUserAsync(factory);
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);
        await RegisterExpectingAsync(first.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.Created);

        using (var withdrawn = await WithdrawAsync(first.Client, matchId))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        }

        var released = Assert.Single(await OutboxPayloadsAsync(factory.Services, matchId, SpotReleased));
        Assert.Equal((matchId, first.Id), (released.GetProperty("matchId").GetGuid(), released.GetProperty("userId").GetGuid()));

        var confirmed = (await OutboxPayloadsAsync(factory.Services, matchId, SpotConfirmed))
            .Select(p => (UserId: p.GetProperty("userId").GetGuid(), Source: p.GetProperty("source").GetString()))
            .OrderBy(p => p.Source, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([(first.Id, "direct"), (second.Id, "waitlist_promotion")], confirmed);
    }

    [Fact]
    public async Task Registration_routes_require_authentication()
    {
        using var anonymous = factory.CreateClient();
        var matchId = Guid.CreateVersion7();

        using var register = await RegisterAsync(anonymous, matchId);
        using var withdraw = await WithdrawAsync(anonymous, matchId);
        using var participants = await anonymous.GetAsync(new Uri($"{MatchesPath}/{matchId}/registrations", UriKind.Relative), Ct);
        using var mine = await anonymous.GetAsync(new Uri($"{MyMatchesPath}?role=organizer", UriKind.Relative), Ct);

        Assert.All([register, withdraw, participants, mine], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }
}
