using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain.Events;
using static Applada.Modules.Matches.Domain.Tests.TestData;

namespace Applada.Modules.Matches.Domain.Tests;

public sealed class MatchEditingTests
{
    private static readonly Registration[] NoWaitlist = [];

    [Fact]
    public void Non_organizer_cannot_edit()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(Guid.NewGuid(), match.Version, "Outro título", null, null, NoWaitlist, Now);

        AssertError(result, "not-organizer", ErrorKind.Forbidden);
        Assert.Equal("Pelada de quinta", match.Title);
        Assert.Equal(1, match.Version);
    }

    [Fact]
    public void Edit_with_stale_version_fails()
    {
        var match = CreateMatch();
        Assert.True(match.ChangeDetails(OrganizerId, 1, "Primeira edição", null, null, NoWaitlist, Now).IsSuccess);

        var result = match.ChangeDetails(OrganizerId, 1, "Edição atrasada", null, null, NoWaitlist, Now);

        AssertError(result, "version-mismatch", ErrorKind.PreconditionFailed);
        Assert.Equal("Primeira edição", match.Title);
        Assert.Equal(2, match.Version);
    }

    [Fact]
    public void Edit_after_start_fails()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(OrganizerId, match.Version, "Tarde demais", null, null, NoWaitlist, match.StartsAt);

        AssertError(result, "registration-closed", ErrorKind.Conflict);
    }

    [Fact]
    public void Edit_cancelled_match_fails()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.ChangeDetails(OrganizerId, match.Version, "Ressuscitada", null, null, NoWaitlist, Now);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
    }

    [Fact]
    public void Edit_checks_organizer_before_cancellation_and_version()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.ChangeDetails(Guid.NewGuid(), 99, null, null, null, NoWaitlist, match.StartsAt);

        AssertError(result, "not-organizer", ErrorKind.Forbidden);
    }

    [Fact]
    public void Edit_checks_version_before_field_validation()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(OrganizerId, 99, "", null, 0, NoWaitlist, Now);

        AssertError(result, "version-mismatch", ErrorKind.PreconditionFailed);
    }

    [Fact]
    public void Edit_rejects_invalid_fields()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(OrganizerId, match.Version, "   ", new string('a', 201), 201, NoWaitlist, Now);

        AssertError(result, "validation", ErrorKind.Validation);
        Assert.Equal(["capacity", "locationText", "title"], result.Error.Fields!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, match.Version);
    }

    [Fact]
    public void Reduce_capacity_below_confirmed_fails()
    {
        var match = CreateMatch(capacity: 5);
        for (var i = 0; i < 3; i++)
        {
            Register(match, Guid.NewGuid());
        }

        var result = match.ChangeDetails(OrganizerId, match.Version, null, null, 2, NoWaitlist, Now);

        AssertError(result, "capacity-below-confirmed", ErrorKind.Conflict);
        Assert.Equal(5, match.Capacity);
        Assert.Equal(1, match.Version);
    }

    [Fact]
    public void Reduce_capacity_to_exactly_confirmed_succeeds()
    {
        var match = CreateMatch(capacity: 5);
        for (var i = 0; i < 3; i++)
        {
            Register(match, Guid.NewGuid());
        }

        var result = match.ChangeDetails(OrganizerId, match.Version, null, null, 3, NoWaitlist, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, match.Capacity);
        Assert.Equal(3, match.ConfirmedCount);
    }

    [Fact]
    public void Increase_capacity_promotes_waitlist_head_in_order()
    {
        var match = CreateMatch(capacity: 2);
        var registrations = Enumerable.Range(0, 5).Select(_ => Register(match, Guid.NewGuid())).ToList();
        var waitlist = registrations.Where(r => r.Status == RegistrationStatus.Waitlisted).OrderBy(r => r.QueueSeq).ToList();
        Assert.Equal(3, waitlist.Count);
        match.ClearDomainEvents();

        var result = match.ChangeDetails(OrganizerId, match.Version, null, null, 4, waitlist, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationStatus.Confirmed, waitlist[0].Status);
        Assert.Equal(RegistrationStatus.Confirmed, waitlist[1].Status);
        Assert.Equal(RegistrationStatus.Waitlisted, waitlist[2].Status);
        Assert.Equal(4, match.ConfirmedCount);
        Assert.Equal(1, match.WaitlistCount);
        Assert.Equal(
            [
                new SpotConfirmed(match.Id, waitlist[0].UserId, ConfirmationSource.WaitlistPromotion),
                new SpotConfirmed(match.Id, waitlist[1].UserId, ConfirmationSource.WaitlistPromotion),
            ],
            match.DomainEvents);
    }

    [Fact]
    public void Increase_capacity_without_enough_waitlist_head_is_a_programming_error()
    {
        var match = CreateMatch(capacity: 1);
        Register(match, Guid.NewGuid());
        var waitlisted = Register(match, Guid.NewGuid());
        Register(match, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => match.ChangeDetails(OrganizerId, match.Version, null, null, 3, [waitlisted], Now));
    }

    [Fact]
    public void Edit_increments_version()
    {
        var match = CreateMatch();
        var later = Now.AddMinutes(5);

        var result = match.ChangeDetails(OrganizerId, 1, "  Novo título  ", "Arena central", 12, NoWaitlist, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, match.Version);
        Assert.Equal(later, match.UpdatedAt);
        Assert.Equal("Novo título", match.Title);
        Assert.Equal("Arena central", match.LocationText);
        Assert.Equal(12, match.Capacity);
    }

    [Fact]
    public void Null_fields_are_left_unchanged()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(OrganizerId, 1, null, null, null, NoWaitlist, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pelada de quinta", match.Title);
        Assert.Equal("Quadra do bairro", match.LocationText);
        Assert.Equal(10, match.Capacity);
    }

    [Fact]
    public void Empty_location_text_clears_location()
    {
        var match = CreateMatch();

        var result = match.ChangeDetails(OrganizerId, match.Version, null, "", null, NoWaitlist, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(match.LocationText);
    }

    [Fact]
    public void Cancel_by_organizer_sets_cancelled_and_emits_event()
    {
        var match = CreateMatch();
        var later = Now.AddHours(1);

        var result = match.Cancel(OrganizerId, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(MatchStatus.Cancelled, match.Status);
        Assert.Equal(later, match.CancelledAt);
        Assert.Equal(2, match.Version);
        Assert.Equal(new MatchCancelled(match.Id), Assert.Single(match.DomainEvents));
    }

    [Fact]
    public void Cancel_by_non_organizer_fails()
    {
        var match = CreateMatch();

        var result = match.Cancel(Guid.NewGuid(), Now);

        AssertError(result, "not-organizer", ErrorKind.Forbidden);
        Assert.Equal(MatchStatus.Scheduled, match.Status);
    }

    [Fact]
    public void Cancel_after_start_fails()
    {
        var match = CreateMatch();

        var result = match.Cancel(OrganizerId, match.StartsAt);

        AssertError(result, "registration-closed", ErrorKind.Conflict);
    }

    [Fact]
    public void Cancel_twice_fails_with_match_cancelled()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);
        match.ClearDomainEvents();

        var result = match.Cancel(OrganizerId, Now);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
        Assert.Equal(2, match.Version);
        Assert.Empty(match.DomainEvents);
    }

    private static void AssertError(Result result, string code, ErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(kind, result.Error.Kind);
    }
}
