using Applada.Modules.Communities.Contracts;

namespace Applada.Modules.Communities.Domain;

/// <summary>Grupo que organiza peladas. <see cref="MemberCount"/> é mantido na mesma transação de cada entrada e saída.</summary>
internal sealed class Group(Guid id, string name, string? description, Guid createdBy, DateTimeOffset createdAt)
{
    public Guid Id { get; private set; } = id;

    public string Name { get; private set; } = name;

    public string? Description { get; private set; } = description;

    public Guid CreatedBy { get; private set; } = createdBy;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public int MemberCount { get; private set; } = 1;
}

/// <summary>Participação de um usuário num grupo. Chave (grupo, usuário); exatamente um <see cref="GroupRole.Owner"/> por grupo.</summary>
internal sealed class Membership(Guid groupId, Guid userId, GroupRole role, DateTimeOffset joinedAt)
{
    public Guid GroupId { get; private set; } = groupId;

    public Guid UserId { get; private set; } = userId;

    public GroupRole Role { get; private set; } = role;

    public DateTimeOffset JoinedAt { get; private set; } = joinedAt;
}

/// <summary>
/// Convite por link. Só o hash SHA-256 do token é guardado; o token sai uma única vez, na criação.
/// <see cref="UseCount"/> só cresce por <c>UPDATE</c> condicional (nunca passa de <see cref="MaxUses"/>).
/// </summary>
internal sealed class Invitation(Guid id, Guid groupId, byte[] tokenHash, Guid createdBy, DateTimeOffset createdAt, DateTimeOffset expiresAt, int maxUses)
{
    public Guid Id { get; private set; } = id;

    public Guid GroupId { get; private set; } = groupId;

    public byte[] TokenHash { get; private set; } = tokenHash;

    public Guid CreatedBy { get; private set; } = createdBy;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;

    public int MaxUses { get; private set; } = maxUses;

    public int UseCount { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }
}
