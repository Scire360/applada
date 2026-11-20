using System.Text;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Communities.Contracts;

namespace Applada.Contracts.Tests;

/// <summary>Congela o JSON publicado do evento v1 do Communities. Mudou o literal: é mudança de contrato.</summary>
public class CommunitiesEventContractTests
{
    private const string EventId = "01930000-0000-7000-8000-000000000001";
    private const string GroupId = "01930000-0000-7000-8000-0000000000cc";
    private const string UserId = "01930000-0000-7000-8000-0000000000bb";

    [Fact]
    public void GroupMemberJoinedV1_envelope_shape()
    {
        var evt = new GroupMemberJoinedV1(Guid.Parse(GroupId), Guid.Parse(UserId), "member");
        var json = Encoding.UTF8.GetString(IntegrationEventEnvelopeSerializer.Serialize(
            OutboxMessage.From(evt, "c-1", new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), Guid.Parse(EventId))));

        Assert.Equal(
            $$$"""{"eventId":"{{{EventId}}}","eventType":"communities.group-member-joined.v1","schemaVersion":1,"aggregateId":"{{{GroupId}}}","occurredAt":"2026-11-20T12:00:00+00:00","correlationId":"c-1","payload":{"groupId":"{{{GroupId}}}","userId":"{{{UserId}}}","role":"member"}}""",
            json);
    }
}
