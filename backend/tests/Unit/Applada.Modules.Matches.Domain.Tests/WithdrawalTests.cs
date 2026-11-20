using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain.Events;
using static Applada.Modules.Matches.Domain.Tests.TestData;

namespace Applada.Modules.Matches.Domain.Tests;

public sealed class WithdrawalTests
{
    [Fact]
    public void Withdraw_confirmed_without_waitlist_frees_spot()
    {
        var match = CreateMatch(capacity: 2);
        var a = Register(match, Guid.NewGuid());
        match.ClearDomainEvents();
        var later = Now.AddMinutes(5);

        var result = match.Withdraw(a, null, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationStatus.Withdrawn, a.Status);
        Assert.Equal(later, a.UpdatedAt);
        Assert.Equal(0, match.ConfirmedCount);
        Assert.Equal(0, match.WaitlistCount);
        Assert.Equal(1, match.Version);
        Assert.Equal(new SpotReleased(match.Id, a.UserId), Assert.Single(match.DomainEvents));
    }

    [Fact]
    public void Withdraw_confirmed_promotes_next_in_line()
    {
        var match = CreateMatch(capacity: 1);
        var a = Register(match, Guid.NewGuid());
        var b = Register(match, Guid.NewGuid());
        var c = Register(match, Guid.NewGuid());
        match.ClearDomainEvents();

        var result = match.Withdraw(a, b, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationStatus.Withdrawn, a.Status);
        Assert.Equal(RegistrationStatus.Confirmed, b.Status);
        Assert.Equal(RegistrationStatus.Waitlisted, c.Status);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
        Assert.Equal(
            [
                new SpotReleased(match.Id, a.UserId),
                new SpotConfirmed(match.Id, b.UserId, ConfirmationSource.WaitlistPromotion),
            ],
            match.DomainEvents);
    }

    [Fact]
    public void Withdraw_waitlisted_does_not_promote()
    {
        var match = CreateMatch(capacity: 1);
        var a = Register(match, Guid.NewGuid());
        var b = Register(match, Guid.NewGuid());
        var c = Register(match, Guid.NewGuid());
        match.ClearDomainEvents();

        var result = match.Withdraw(b, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.Equal(RegistrationStatus.Withdrawn, b.Status);
        Assert.Equal(RegistrationStatus.Waitlisted, c.Status);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Withdraw_already_withdrawn_fails_with_registration_not_found()
    {
        var match = CreateMatch();
        var a = Register(match, Guid.NewGuid());
        Assert.True(match.Withdraw(a, null, Now).IsSuccess);
        match.ClearDomainEvents();

        var result = match.Withdraw(a, null, Now);

        AssertError(result, "registration-not-found", ErrorKind.NotFound);
        Assert.Equal(0, match.ConfirmedCount);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Withdraw_registration_of_another_match_fails_with_registration_not_found()
    {
        var match = CreateMatch();
        var other = CreateMatch();
        var registration = Register(other, Guid.NewGuid());

        var result = match.Withdraw(registration, null, Now);

        AssertError(result, "registration-not-found", ErrorKind.NotFound);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.Equal(1, other.ConfirmedCount);
    }

    [Fact]
    public void Withdraw_without_registration_on_open_match_fails_with_registration_not_found()
    {
        var match = CreateMatch();

        var result = match.Withdraw(null, null, Now);

        AssertError(result, "registration-not-found", ErrorKind.NotFound);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Withdraw_without_registration_after_start_fails_with_registration_closed()
    {
        var match = CreateMatch();

        var result = match.Withdraw(null, null, match.StartsAt);

        AssertError(result, "registration-closed", ErrorKind.Conflict);
    }

    [Fact]
    public void Withdraw_without_registration_on_cancelled_match_fails_with_match_cancelled()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);
        match.ClearDomainEvents();

        var result = match.Withdraw(null, null, Now);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Withdraw_after_start_fails()
    {
        var match = CreateMatch();
        var a = Register(match, Guid.NewGuid());

        var result = match.Withdraw(a, null, match.StartsAt);

        AssertError(result, "registration-closed", ErrorKind.Conflict);
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.Equal(1, match.ConfirmedCount);
    }

    [Fact]
    public void Withdraw_on_cancelled_match_fails()
    {
        var match = CreateMatch();
        var a = Register(match, Guid.NewGuid());
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.Withdraw(a, null, match.StartsAt);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
    }

    [Fact]
    public void Next_in_line_not_waitlisted_is_a_programming_error()
    {
        var match = CreateMatch(capacity: 2);
        var a = Register(match, Guid.NewGuid());
        var b = Register(match, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => match.Withdraw(a, b, Now));
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.Equal(2, match.ConfirmedCount);
    }

    [Fact]
    public void Withdraw_confirmed_without_next_in_line_while_waitlist_exists_is_a_programming_error()
    {
        var match = CreateMatch(capacity: 1);
        var a = Register(match, Guid.NewGuid());
        Register(match, Guid.NewGuid());

        Assert.Throws<MatchStateDivergenceException>(() => match.Withdraw(a, null, Now));
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
    }

    [Fact]
    public void Counters_match_registration_states_after_random_sequence()
    {
        const int Capacity = 10;
        var random = new Random(20261113);
        var match = CreateMatch(capacity: Capacity);
        var users = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        var registrations = new Dictionary<Guid, Registration>();
        var lastQueueSeq = 0L;
        var promotions = 0;
        var now = Now;

        for (var step = 0; step < 200; step++)
        {
            now = now.AddSeconds(1);
            var userId = users[random.Next(users.Length)];
            registrations.TryGetValue(userId, out var existing);
            var head = registrations.Values
                .Where(r => r.Status == RegistrationStatus.Waitlisted)
                .MinBy(r => r.QueueSeq);
            match.ClearDomainEvents();

            var isActive = existing is { Status: RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted };
            if (isActive && random.Next(10) < 7)
            {
                var nextInLine = existing!.Status == RegistrationStatus.Confirmed ? head : null;
                Assert.True(match.Withdraw(existing, nextInLine, now).IsSuccess);
                Assert.Equal(RegistrationStatus.Withdrawn, existing.Status);
            }
            else
            {
                var result = match.Register(userId, existing, now);
                Assert.True(result.IsSuccess);
                Assert.Equal(!isActive, result.Value.Changed);
                if (result.Value.Changed)
                {
                    // FIFO: toda (re)inscrição entra depois de todas as anteriores.
                    Assert.True(result.Value.Registration.QueueSeq > lastQueueSeq, $"Passo {step}: QueueSeq fora de ordem.");
                    lastQueueSeq = result.Value.Registration.QueueSeq;
                    registrations[userId] = result.Value.Registration;
                }
            }

            // FIFO: a promovida é sempre a da espera com menor QueueSeq antes do passo.
            foreach (var promoted in match.DomainEvents.OfType<SpotConfirmed>().Where(e => e.Source == ConfirmationSource.WaitlistPromotion))
            {
                Assert.NotNull(head);
                Assert.Equal(head.UserId, promoted.UserId);
                Assert.Equal(RegistrationStatus.Confirmed, head.Status);
                promotions++;
            }

            var confirmed = registrations.Values.Count(r => r.Status == RegistrationStatus.Confirmed);
            var waitlisted = registrations.Values.Count(r => r.Status == RegistrationStatus.Waitlisted);
            Assert.True(confirmed == match.ConfirmedCount, $"Passo {step}: ConfirmedCount {match.ConfirmedCount} != {confirmed} confirmados.");
            Assert.True(waitlisted == match.WaitlistCount, $"Passo {step}: WaitlistCount {match.WaitlistCount} != {waitlisted} na espera.");
            Assert.True(match.ConfirmedCount <= Capacity, $"Passo {step}: {match.ConfirmedCount} confirmados com capacidade {Capacity}.");
            Assert.True(match.WaitlistCount == 0 || match.ConfirmedCount == Capacity, $"Passo {step}: vaga livre com gente na espera.");
            Assert.True(match.NextQueueSeq > lastQueueSeq, $"Passo {step}: NextQueueSeq não avançou.");
        }

        Assert.True(promotions >= 10, $"A sequência deveria exercitar a promoção; houve só {promotions}.");
    }

    private static void AssertError(Result result, string code, ErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(kind, result.Error.Kind);
    }
}
