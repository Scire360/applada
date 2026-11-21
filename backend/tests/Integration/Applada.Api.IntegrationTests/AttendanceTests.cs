using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public sealed class AttendanceTests(ApiFactory factory) : IAsyncLifetime
{
    private const string EventType = "matches.attendance-recorded.v1";
    private readonly string _subject = NewSubject("chamada");
    private HttpClient _organizer = null!;
    private TestUser _player = null!;
    private DateTimeOffset _originalTime;
    private DateTimeOffset _startsAt;
    private Guid _match;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private string Path => $"{MatchesPath}/{_match}/attendance";

    public async ValueTask InitializeAsync()
    {
        _originalTime = factory.Clock.GetUtcNow();
        _startsAt = _originalTime.AddMinutes(2);
        _organizer = factory.CreateClientFor(_subject);
        _player = await NewUserAsync(factory);
        _match = await CreateMatchAsync(_organizer, _startsAt, capacity: 2);
        await RegisterExpectingAsync(_player.Client, _match, HttpStatusCode.Created);
    }

    public ValueTask DisposeAsync()
    {
        factory.Clock.SetUtcNow(_originalTime);
        _organizer.Dispose();
        _player.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Organizer_marks_present_then_absent()
    {
        OpenAttendance();
        var present = await MarkAsync("present");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var absent = await MarkAsync("absent");
        Assert.True(present < absent);
        using var page = await ReadPageAsync();
        var item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("absent", item.GetProperty("status").GetString());
        Assert.Equal(absent, item.GetProperty("recordedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Same_status_twice_returns_200_and_single_outbox_event()
    {
        OpenAttendance();
        var first = await MarkAsync("present");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(first, await MarkAsync("present"));
        Assert.Single(await OutboxPayloadsAsync(factory.Services, _match, EventType));
    }

    [Fact]
    public async Task Changed_status_writes_second_event_with_payload()
    {
        OpenAttendance();
        var first = await MarkAsync("present");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await MarkAsync("absent");
        var events = await OutboxPayloadsAsync(factory.Services, _match, EventType);
        Assert.Equal(2, events.Count);
        foreach (var (status, time) in new[] { ("present", first), ("absent", second) })
        {
            var payload = Assert.Single(events, e => e.GetProperty("status").GetString() == status);
            Assert.Equal(_match, payload.GetProperty("matchId").GetGuid());
            Assert.Equal(_player.Id, payload.GetProperty("userId").GetGuid());
            Assert.Equal(time, payload.GetProperty("recordedAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task Before_start_returns_409_attendance_not_open()
    {
        using var response = await PutAsync(_organizer, _player.Id);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "attendance-not-open");
    }

    [Fact]
    public async Task After_window_returns_409_attendance_closed()
    {
        factory.Clock.SetUtcNow(_startsAt.AddHours(25));
        using var renewed = factory.CreateClientFor(_subject);
        using var response = await PutAsync(renewed, _player.Id);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "attendance-closed");
    }

    [Fact]
    public async Task Cancelled_match_returns_409()
    {
        using var cancelled = await _organizer.PostAsync($"{MatchesPath}/{_match}/cancellation", null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        OpenAttendance();
        using var response = await PutAsync(_organizer, _player.Id);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "match-cancelled");
    }

    [Fact]
    public async Task Non_organizer_put_and_get_return_403()
    {
        OpenAttendance();
        await AssertForbiddenAsync(_player.Client);
    }

    [Fact]
    public async Task Organizer_of_other_match_cannot_mark_or_read()
    {
        using var other = await NewUserAsync(factory);
        await CreateMatchAsync(other.Client, _startsAt, capacity: 2);
        OpenAttendance();
        await AssertForbiddenAsync(other.Client);
    }

    [Fact]
    public async Task Waitlisted_user_returns_404_attendance_participant_not_found()
    {
        using var confirmed = await NewUserAsync(factory);
        using var waiting = await NewUserAsync(factory);
        await RegisterExpectingAsync(confirmed.Client, _match, HttpStatusCode.Created);
        var registration = await RegisterExpectingAsync(waiting.Client, _match, HttpStatusCode.Created);
        Assert.Equal("waitlisted", registration.Status);
        OpenAttendance();
        using var response = await PutAsync(_organizer, waiting.Id);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "attendance-participant-not-found");
    }

    [Fact]
    public async Task Withdrawn_user_returns_404_attendance_participant_not_found()
    {
        using var withdrawn = await WithdrawAsync(_player.Client, _match);
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        OpenAttendance();
        using var response = await PutAsync(_organizer, _player.Id);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "attendance-participant-not-found");
    }

    [Fact]
    public async Task Unknown_match_returns_404()
    {
        _match = Guid.NewGuid();
        OpenAttendance();
        using var put = await PutAsync(_organizer, _player.Id);
        using var get = await _organizer.GetAsync(Path, Ct);
        using var putProblem = await AssertProblemAsync(put, HttpStatusCode.NotFound, "match-not-found");
        using var getProblem = await AssertProblemAsync(get, HttpStatusCode.NotFound, "match-not-found");
    }

    [Theory]
    [InlineData("Present")]
    [InlineData("waitlisted")]
    [InlineData("")]
    public async Task Invalid_status_returns_400(string status)
    {
        OpenAttendance();
        using var response = await PutAsync(_organizer, _player.Id, status);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Anonymous_returns_401()
    {
        using var anonymous = factory.CreateClient();
        using var put = await PutAsync(anonymous, _player.Id);
        using var get = await anonymous.GetAsync(Path, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    [Fact]
    public async Task List_rejects_cursors_from_registrations()
    {
        using var second = await NewUserAsync(factory);
        using var waiting = await NewUserAsync(factory);
        using var last = await NewUserAsync(factory);
        await RegisterExpectingAsync(second.Client, _match, HttpStatusCode.Created);
        await RegisterExpectingAsync(waiting.Client, _match, HttpStatusCode.Created);
        await RegisterExpectingAsync(last.Client, _match, HttpStatusCode.Created);

        foreach (var (limit, status) in new[] { (3, "waitlisted"), (1, "confirmed") })
        {
            using var registrations = await _organizer.GetAsync($"{MatchesPath}/{_match}/registrations?limit={limit}", Ct);
            registrations.EnsureSuccessStatusCode();
            using var page = await ReadJsonAsync(registrations);
            Assert.Equal(status, page.RootElement.GetProperty("items")[limit - 1].GetProperty("status").GetString());
            var cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);

            using var response = await _organizer.GetAsync($"{Path}?cursor={cursor}", Ct);
            using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid-cursor");
        }
    }

    [Fact]
    public async Task List_shows_confirmed_with_null_until_marked_and_paginates()
    {
        using var second = await NewUserAsync(factory);
        using var waiting = await NewUserAsync(factory);
        using var profile = await second.Client.PutAsJsonAsync("/api/v1/me/profile", new { displayName = "Jogadora da chamada" }, Ct);
        profile.EnsureSuccessStatusCode();
        await RegisterExpectingAsync(second.Client, _match, HttpStatusCode.Created);
        await RegisterExpectingAsync(waiting.Client, _match, HttpStatusCode.Created);
        OpenAttendance();
        var marked = await MarkAsync("present");
        using var first = await ReadPageAsync("?limit=1");
        var entry = Assert.Single(first.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(_player.Id, entry.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("displayName").ValueKind);
        Assert.Equal("present", entry.GetProperty("status").GetString());
        Assert.Equal(marked, entry.GetProperty("recordedAt").GetDateTimeOffset());

        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        using var next = await ReadPageAsync($"?limit=1&cursor={cursor}");
        var unmarked = Assert.Single(next.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(second.Id, unmarked.GetProperty("userId").GetGuid());
        Assert.Equal("Jogadora da chamada", unmarked.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("recordedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, next.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("absent", 2)]
    [InlineData("present", 1)]
    public async Task Concurrent_puts_for_same_user_leave_one_row(string secondStatus, int expectedEvents)
    {
        OpenAttendance();
        await Task.WhenAll(MarkAsync("present"), MarkAsync(secondStatus));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var recorder = await db.Database.SqlQuery<Guid>($"""
            SELECT recorded_by AS "Value" FROM matches.attendance
            WHERE match_id = {_match} AND user_id = {_player.Id}
            """).SingleAsync(Ct);
        Assert.Equal(await GetUserIdAsync(_organizer), recorder);
        Assert.Equal(expectedEvents, (await OutboxPayloadsAsync(factory.Services, _match, EventType)).Count);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Conflict, "match-cancelled")]
    [InlineData(false, HttpStatusCode.NotFound, "attendance-participant-not-found")]
    public async Task Pre_start_change_committed_during_first_mark_is_respected(bool cancel, HttpStatusCode expected, string code)
    {
        var actorId = await GetUserIdAsync(_organizer);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var match = await db.Matches.FromSql($"SELECT * FROM matches.matches WHERE id = {_match} FOR UPDATE").SingleAsync(Ct);
        var registration = await db.Registrations.SingleAsync(r => r.MatchId == _match && r.UserId == _player.Id, Ct);
        var before = factory.Clock.GetUtcNow();
        if (cancel)
        {
            Assert.True(match.Cancel(actorId, before).IsSuccess);
        }
        else
        {
            Assert.True(match.Withdraw(registration, null, before).IsSuccess);
        }

        await db.SaveChangesAsync(Ct);
        OpenAttendance();
        var pending = PutAsync(_organizer, _player.Id);
        // Libera a alteração pré-jogo somente quando o banco confirmar que o PUT a está aguardando.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pg_backend_pid() = ANY(pg_blocking_pids(pid))) AS "Value"
            """).SingleAsync(timeout.Token))
        {
            await Task.Delay(10, timeout.Token);
        }

        await tx.CommitAsync(Ct);
        using var response = await pending;
        using var problem = await AssertProblemAsync(response, expected, code);
        Assert.Empty(await OutboxPayloadsAsync(factory.Services, _match, EventType));
        Assert.False(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM matches.attendance WHERE match_id = {_match}) AS "Value"
            """).SingleAsync(Ct));
    }

    private void OpenAttendance() => factory.Clock.SetUtcNow(_startsAt);

    private Task<HttpResponseMessage> PutAsync(HttpClient client, Guid userId, string status = "present") =>
        client.PutAsJsonAsync(new Uri($"{Path}/{userId}", UriKind.Relative), new { status }, Ct);

    private async Task<DateTimeOffset> MarkAsync(string status)
    {
        using var response = await PutAsync(_organizer, _player.Id, status);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(_player.Id, body.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetString());
        return body.RootElement.GetProperty("recordedAt").GetDateTimeOffset();
    }

    private async Task<JsonDocument> ReadPageAsync(string query = "")
    {
        using var response = await _organizer.GetAsync(Path + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadJsonAsync(response);
    }

    private async Task AssertForbiddenAsync(HttpClient client)
    {
        using var put = await PutAsync(client, _player.Id);
        using var get = await client.GetAsync(Path, Ct);
        using var putProblem = await AssertProblemAsync(put, HttpStatusCode.Forbidden, "not-organizer");
        using var getProblem = await AssertProblemAsync(get, HttpStatusCode.Forbidden, "not-organizer");
        Assert.Empty(await OutboxPayloadsAsync(factory.Services, _match, EventType));
    }
}
