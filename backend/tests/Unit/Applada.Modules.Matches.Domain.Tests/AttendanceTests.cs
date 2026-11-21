using Applada.BuildingBlocks.Domain;
using static Applada.Modules.Matches.Domain.Tests.TestData;

namespace Applada.Modules.Matches.Domain.Tests;

public sealed class AttendanceTests
{
    [Theory]
    [InlineData("present", AttendanceStatus.Present)]
    [InlineData("absent", AttendanceStatus.Absent)]
    public void Attendance_status_round_trips_its_contract_text(string text, AttendanceStatus status)
    {
        Assert.Equal(status, AttendanceStatusText.Parse(text));
        Assert.Equal(text, status.ToContractText());
    }

    [Fact]
    public void Organizer_can_record_at_exact_start()
    {
        var match = CreateMatch();
        var registration = Register(match, Guid.NewGuid());
        match.ClearDomainEvents();
        var version = match.Version;

        var result = match.CanRecordAttendance(OrganizerId, registration, match.StartsAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(version, match.Version);
        Assert.Equal(Now, match.UpdatedAt);
        Assert.Empty(match.DomainEvents);
        Assert.Equal(1, match.ConfirmedCount);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.Equal(Now, registration.UpdatedAt);
    }

    [Fact]
    public void Before_start_is_not_open()
    {
        var match = CreateMatch();

        var result = match.CanRecordAttendance(OrganizerId, null, match.StartsAt.AddTicks(-1));

        AssertError(result, "attendance-not-open", ErrorKind.Conflict);
    }

    [Fact]
    public void At_end_plus_24h_exact_is_closed()
    {
        var match = CreateMatch();
        var close = match.StartsAt.AddMinutes(match.DurationMinutes).AddHours(24);

        Assert.Equal(close, match.AttendanceClosesAt);
        var result = match.CanRecordAttendance(OrganizerId, null, close);

        AssertError(result, "attendance-closed", ErrorKind.Conflict);
    }

    [Fact]
    public void After_end_plus_24h_is_closed()
    {
        var match = CreateMatch();
        var registration = Register(match, Guid.NewGuid());
        var close = match.StartsAt.AddMinutes(match.DurationMinutes).AddHours(24);

        var result = match.CanRecordAttendance(OrganizerId, registration, close.AddTicks(1));

        AssertError(result, "attendance-closed", ErrorKind.Conflict);
    }

    [Fact]
    public void One_tick_before_close_is_allowed()
    {
        var match = CreateMatch();
        var registration = Register(match, Guid.NewGuid());
        var close = match.StartsAt.AddMinutes(match.DurationMinutes).AddHours(24);

        var result = match.CanRecordAttendance(OrganizerId, registration, close.AddTicks(-1));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Non_organizer_is_rejected_even_when_cancelled()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.CanRecordAttendance(Guid.NewGuid(), null, Now);

        AssertError(result, "not-organizer", ErrorKind.Forbidden);
    }

    [Fact]
    public void Non_organizer_is_rejected_outside_attendance_window()
    {
        var match = CreateMatch();
        var player = Guid.NewGuid();
        var registration = Register(match, player);

        foreach (var instant in new[] { match.StartsAt.AddTicks(-1), match.AttendanceClosesAt })
        {
            var result = match.CanRecordAttendance(player, registration, instant);
            AssertError(result, "not-organizer", ErrorKind.Forbidden);
        }
    }

    [Fact]
    public void Cancelled_match_is_rejected()
    {
        var match = CreateMatch();
        Assert.True(match.Cancel(OrganizerId, Now).IsSuccess);

        var result = match.CanRecordAttendance(OrganizerId, null, Now);

        AssertError(result, "match-cancelled", ErrorKind.Conflict);
    }

    [Fact]
    public void Waitlisted_registration_is_not_found()
    {
        var match = CreateMatch(capacity: 1);
        Register(match, Guid.NewGuid());
        var waitlisted = Register(match, Guid.NewGuid());

        var result = match.CanRecordAttendance(OrganizerId, waitlisted, match.StartsAt);

        AssertError(result, "attendance-participant-not-found", ErrorKind.NotFound);
    }

    [Fact]
    public void Withdrawn_registration_is_not_found()
    {
        var match = CreateMatch();
        var registration = Register(match, Guid.NewGuid());
        Assert.True(match.Withdraw(registration, null, Now).IsSuccess);

        var result = match.CanRecordAttendance(OrganizerId, registration, match.StartsAt);

        AssertError(result, "attendance-participant-not-found", ErrorKind.NotFound);
    }

    [Fact]
    public void Null_registration_is_not_found()
    {
        var match = CreateMatch();

        var result = match.CanRecordAttendance(OrganizerId, null, match.StartsAt);

        AssertError(result, "attendance-participant-not-found", ErrorKind.NotFound);
    }

    [Fact]
    public void Confirmed_registration_from_another_match_is_not_found()
    {
        var match = CreateMatch();
        var otherRegistration = Register(CreateMatch(), Guid.NewGuid());

        var result = match.CanRecordAttendance(OrganizerId, otherRegistration, match.StartsAt);

        AssertError(result, "attendance-participant-not-found", ErrorKind.NotFound);
    }

    private static void AssertError(Result result, string code, ErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(kind, result.Error.Kind);
    }
}
