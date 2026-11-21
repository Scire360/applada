using System.Text;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Matches.Contracts;

namespace Applada.Contracts.Tests;

/// <summary>
/// Congela o JSON publicado de cada evento v1 de Matches. Mudou o literal: é mudança de contrato e exige nova versão.
/// </summary>
public class MatchesEventContractTests
{
    private const string EventId = "01930000-0000-7000-8000-000000000001";
    private const string MatchId = "01930000-0000-7000-8000-0000000000aa";
    private const string UserId = "01930000-0000-7000-8000-0000000000bb";

    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MatchCreatedV1_envelope_shape()
    {
        var evt = new MatchCreatedV1(Guid.Parse(MatchId), Guid.Parse(UserId), new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero), 20, "public");

        // O payload sai do IntegrationEventJson (encoder padrão), que escapa '+' (barra invertida + u002B): mesmo valor ao ler o JSON.
        var escapedPlus = (char)92 + "u002B";
        Assert.Equal(
            Envelope("matches.match-created.v1", $$"""{"matchId":"{{MatchId}}","organizerId":"{{UserId}}","startsAt":"2026-11-20T21:30:00{{escapedPlus}}00:00","capacity":20,"visibility":"public"}"""),
            Serialize(evt));
    }

    [Theory]
    [InlineData(SpotConfirmedV1.SourceDirect, "direct")]
    [InlineData(SpotConfirmedV1.SourceWaitlistPromotion, "waitlist_promotion")]
    public void SpotConfirmedV1_envelope_shape(string source, string expectedSource)
    {
        var evt = new SpotConfirmedV1(Guid.Parse(MatchId), Guid.Parse(UserId), source);

        Assert.Equal(
            Envelope("matches.spot-confirmed.v1", $$"""{"matchId":"{{MatchId}}","userId":"{{UserId}}","source":"{{expectedSource}}"}"""),
            Serialize(evt));
    }

    [Fact]
    public void SpotReleasedV1_envelope_shape()
    {
        var evt = new SpotReleasedV1(Guid.Parse(MatchId), Guid.Parse(UserId));

        Assert.Equal(
            Envelope("matches.spot-released.v1", $$"""{"matchId":"{{MatchId}}","userId":"{{UserId}}"}"""),
            Serialize(evt));
    }

    [Fact]
    public void MatchCancelledV1_envelope_shape()
    {
        var evt = new MatchCancelledV1(Guid.Parse(MatchId));

        Assert.Equal(
            Envelope("matches.match-cancelled.v1", $$"""{"matchId":"{{MatchId}}"}"""),
            Serialize(evt));
    }

    [Theory]
    [InlineData("present")]
    [InlineData("absent")]
    public void AttendanceRecordedV1_envelope_shape(string status)
    {
        var evt = new AttendanceRecordedV1(Guid.Parse(MatchId), Guid.Parse(UserId), status, OccurredAt);
        var escapedPlus = (char)92 + "u002B";
        Assert.Equal(
            Envelope("matches.attendance-recorded.v1", $$"""{"matchId":"{{MatchId}}","userId":"{{UserId}}","status":"{{status}}","recordedAt":"2026-11-13T12:00:00{{escapedPlus}}00:00"}"""),
            Serialize(evt));
    }

    private static string Serialize(IntegrationEvent evt) =>
        Encoding.UTF8.GetString(IntegrationEventEnvelopeSerializer.Serialize(OutboxMessage.From(evt, "c-1", OccurredAt, Guid.Parse(EventId))));

    private static string Envelope(string eventType, string payload) =>
        $$"""{"eventId":"{{EventId}}","eventType":"{{eventType}}","schemaVersion":1,"aggregateId":"{{MatchId}}","occurredAt":"2026-11-13T12:00:00+00:00","correlationId":"c-1","payload":{{payload}}}""";
}
