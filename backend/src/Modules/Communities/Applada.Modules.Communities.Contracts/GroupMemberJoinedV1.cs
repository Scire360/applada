using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Communities.Contracts;

/// <summary>
/// Usuário entrou no grupo: ao criar (como <c>owner</c>) ou ao aceitar um convite (como <c>member</c>).
/// Payload: <c>{ "groupId": "...", "userId": "...", "role": "owner|member" }</c>, sem PII.
/// </summary>
public sealed record GroupMemberJoinedV1(Guid GroupId, Guid UserId, string Role) : IntegrationEvent
{
    public const string Type = "communities.group-member-joined.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => GroupId;
}
