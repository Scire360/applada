using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain.Events;
using static Applada.Modules.Matches.Domain.Tests.TestData;

namespace Applada.Modules.Matches.Domain.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Register_with_free_spot_confirms()
    {
        var match = CreateMatch(capacity: 2);
        var userId = Guid.NewGuid();
        var later = Now.AddMinutes(1);

        var result = match.Register(userId, null, later);

        Assert.True(result.IsSuccess);
        var (registration, changed) = result.Value;
        Assert.True(changed);
        Assert.NotEqual(Guid.Empty, registration.Id);
        Assert.Equal(match.Id, registration.MatchId);
        Assert.Equal(userId, registration.UserId);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.Equal(1, registration.QueueSeq);
        Assert.Equal(later, registration.CreatedAt);
        Assert.Equal(later, registration.UpdatedAt);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(0, match.WaitlistCount);
        Assert.Equal(2, match.NextQueueSeq);
        Assert.Equal(1, match.Version);
        Assert.Equal(new SpotConfirmed(match.Id, userId, ConfirmationSource.Direct), Assert.Single(match.DomainEvents));
    }

    [Fact]
    public void Register_when_full_goes_to_waitlist()
    {
        var match = CreateMatch(capacity: 1);
        Register(match, Guid.NewGuid());
        match.ClearDomainEvents();

        var result = match.Register(Guid.NewGuid(), null, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.Equal(RegistrationStatus.Waitlisted, result.Value.Registration.Status);
        Assert.Equal(2, result.Value.Registration.QueueSeq);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Register_twice_returns_unchanged()
    {
        var match = CreateMatch(capacity: 1);
        var userId = Guid.NewGuid();
        var first = Register(match, userId);
        var waitlisted = Register(match, Guid.NewGuid());
        match.ClearDomainEvents();

        var again = match.Register(userId, first, Now.AddMinutes(1));
        var againWaitlisted = match.Register(waitlisted.UserId, waitlisted, Now.AddMinutes(1));

        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Changed);
        Assert.Same(first, again.Value.Registration);
        Assert.Equal(RegistrationStatus.Confirmed, first.Status);
        Assert.Equal(Now, first.UpdatedAt);
        Assert.True(againWaitlisted.IsSuccess);
        Assert.False(againWaitlisted.Value.Changed);
        Assert.Equal(RegistrationStatus.Waitlisted, waitlisted.Status);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
        Assert.Equal(3, match.NextQueueSeq);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Waitlist_is_fifo_by_queue_seq()
    {
        var match = CreateMatch(capacity: 1);
        Register(match, Guid.NewGuid());

        var waitlist = Enumerable.Range(0, 5).Select(_ => Register(match, Guid.NewGuid())).ToList();

        Assert.All(waitlist, r => Assert.Equal(RegistrationStatus.Waitlisted, r.Status));
        Assert.Equal([2L, 3L, 4L, 5L, 6L], waitlist.Select(r => r.QueueSeq));
        Assert.Equal(7, match.NextQueueSeq);
    }

    [Fact]
    public void Register_after_start_fails()
    {
        var match = CreateMatch();

        var result = match.Register(Guid.NewGuid(), null, match.StartsAt);

        AssertError(result, "registration-closed", ErrorKind.Conflict);
        Assert.Equal(0, match.ConfirmedCount);
        Assert.Equal(1, match.NextQueueSeq);
    }

    [Fact]
    public void Register_on_cancelled_match_fails()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.Register(Guid.NewGuid(), null, match.StartsAt);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
    }

    [Fact]
    public void Waitlist_limit_50_returns_waitlist_full()
    {
        var match = CreateMatch(capacity: 1);
        Register(match, Guid.NewGuid());
        for (var i = 0; i < MatchLimits.WaitlistMax; i++)
        {
            Register(match, Guid.NewGuid());
        }

        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(50, match.WaitlistCount);
        var nextQueueSeq = match.NextQueueSeq;
        match.ClearDomainEvents();

        var result = match.Register(Guid.NewGuid(), null, Now);

        AssertError(result, "waitlist-full", ErrorKind.Conflict);
        Assert.Equal(50, match.WaitlistCount);
        Assert.Equal(nextQueueSeq, match.NextQueueSeq);
        Assert.Empty(match.DomainEvents);
    }

    [Fact]
    public void Organizer_can_register_and_occupies_spot()
    {
        var match = CreateMatch(capacity: 1);
        Assert.Equal(0, match.ConfirmedCount);

        var organizer = Register(match, OrganizerId);
        var other = Register(match, Guid.NewGuid());

        Assert.Equal(RegistrationStatus.Confirmed, organizer.Status);
        Assert.Equal(RegistrationStatus.Waitlisted, other.Status);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
    }

    [Fact]
    public void Reregister_after_withdraw_on_full_match_goes_to_end_of_queue()
    {
        var match = CreateMatch(capacity: 1);
        var a = Register(match, Guid.NewGuid());
        var b = Register(match, Guid.NewGuid());
        var c = Register(match, Guid.NewGuid());
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.True(b.QueueSeq < c.QueueSeq);
        Assert.True(match.Withdraw(b, null, Now).IsSuccess);
        var later = Now.AddMinutes(10);

        var result = match.Register(b.UserId, b, later);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.Same(b, result.Value.Registration);
        Assert.Equal(RegistrationStatus.Waitlisted, b.Status);
        Assert.True(b.QueueSeq > c.QueueSeq);
        Assert.Equal(later, b.UpdatedAt);
        Assert.Equal(Now, b.CreatedAt);
        Assert.Equal(2, match.WaitlistCount);
    }

    [Fact]
    public void Reregister_after_withdraw_with_free_spot_confirms_same_registration()
    {
        var match = CreateMatch(capacity: 2);
        var a = Register(match, Guid.NewGuid());
        var id = a.Id;
        Assert.True(match.Withdraw(a, null, Now).IsSuccess);
        match.ClearDomainEvents();

        var result = match.Register(a.UserId, a, Now);

        Assert.True(result.IsSuccess);
        Assert.Same(a, result.Value.Registration);
        Assert.Equal(id, a.Id);
        Assert.Equal(RegistrationStatus.Confirmed, a.Status);
        Assert.Equal(2, a.QueueSeq);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(new SpotConfirmed(match.Id, a.UserId, ConfirmationSource.Direct), Assert.Single(match.DomainEvents));
    }

    [Fact]
    public void Register_with_registration_of_another_user_is_a_programming_error()
    {
        var match = CreateMatch();
        var registration = Register(match, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => match.Register(Guid.NewGuid(), registration, Now));
    }

    private static void AssertError(Result<RegistrationOutcome> result, string code, ErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(kind, result.Error.Kind);
    }
}
