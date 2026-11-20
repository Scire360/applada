using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Applada.BuildingBlocks.Domain;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Communities.Contracts;
using Applada.Modules.Communities.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Communities.Infrastructure;

internal sealed record GroupView(Guid Id, string Name, string? Description, int MemberCount, GroupRole MyRole, DateTimeOffset CreatedAt);

internal sealed record MemberView(Guid UserId, GroupRole Role, DateTimeOffset JoinedAt);

internal sealed record InvitationCreated(Guid Id, string Token, DateTimeOffset ExpiresAt, int MaxUses);

internal enum AcceptOutcome
{
    Joined,
    AlreadyMember,
    Invalid,
    Full,
}

/// <summary>
/// Persistência do Communities. Regras de concorrência:
/// <list type="bullet">
/// <item>Aceite de convite: <c>UPDATE</c> condicional do uso do convite e da contagem de membros, na mesma transação da
/// inserção; o lock de linha do <c>UPDATE</c> serializa aceites concorrentes e a condição é reavaliada depois da espera
/// (READ COMMITTED), então nem o convite passa de <c>max_uses</c> nem o grupo passa do limite.</item>
/// <item>Mudança de papel e remoção travam a linha do grupo (<c>FOR UPDATE</c>) antes de ler os papéis.</item>
/// </list>
/// </summary>
internal sealed class GroupStore(CommunitiesDbContext db) : IGroupMembership
{
    public async Task<GroupRole?> GetRoleAsync(Guid groupId, Guid userId, CancellationToken ct) =>
        await db.Memberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .Select(m => (GroupRole?)m.Role)
            .SingleOrDefaultAsync(ct);

    public async Task<GroupView> CreateAsync(Guid userId, string name, string? description, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var group = new Group(Guid.CreateVersion7(now), name, description, userId, now);
        db.Groups.Add(group);
        db.Memberships.Add(new Membership(group.Id, userId, GroupRole.Owner, now));
        db.OutboxMessages.Add(OutboxMessage.From(new GroupMemberJoinedV1(group.Id, userId, GroupRules.ToText(GroupRole.Owner)), correlationId, now));
        await db.SaveChangesAsync(ct);
        return new GroupView(group.Id, group.Name, group.Description, group.MemberCount, GroupRole.Owner, group.CreatedAt);
    }

    public Task<GroupView?> FindForMemberAsync(Guid groupId, Guid userId, CancellationToken ct) =>
        (from m in db.Memberships.AsNoTracking()
         join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
         where m.GroupId == groupId && m.UserId == userId
         select new GroupView(g.Id, g.Name, g.Description, g.MemberCount, m.Role, g.CreatedAt))
        .SingleOrDefaultAsync(ct);

    /// <summary>Grupos do usuário, do mais recente para o mais antigo: <c>(joined_at, group_id)</c> decrescente.</summary>
    public async Task<IReadOnlyList<(GroupView Group, DateTimeOffset JoinedAt)>> ListMineAsync(
        Guid userId, (DateTimeOffset JoinedAt, Guid GroupId)? after, int limit, CancellationToken ct)
    {
        var memberships = after is { } cursor
            ? db.Memberships.FromSql($"""
                SELECT * FROM communities.memberships
                WHERE user_id = {userId} AND (joined_at, group_id) < ({cursor.JoinedAt}, {cursor.GroupId})
                """)
            : db.Memberships.Where(m => m.UserId == userId);

        var rows = await (from m in memberships.AsNoTracking()
                          join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
                          orderby m.JoinedAt descending, m.GroupId descending
                          select new { View = new GroupView(g.Id, g.Name, g.Description, g.MemberCount, m.Role, g.CreatedAt), m.JoinedAt })
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(r => (r.View, r.JoinedAt))];
    }

    /// <summary>Membros em ordem estável: owner, admins, membros; dentro do papel, por entrada e id.</summary>
    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(
        Guid groupId, (int Rank, DateTimeOffset JoinedAt, Guid UserId)? after, int limit, CancellationToken ct)
    {
        var memberships = after is { } cursor
            ? db.Memberships.FromSql($"""
                SELECT * FROM communities.memberships
                WHERE group_id = {groupId}
                  AND (CASE role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, joined_at, user_id)
                      > ({cursor.Rank}, {cursor.JoinedAt}, {cursor.UserId})
                """)
            : db.Memberships.Where(m => m.GroupId == groupId);

        return await memberships.AsNoTracking()
            .OrderBy(m => m.Role == GroupRole.Owner ? 0 : m.Role == GroupRole.Admin ? 1 : 2)
            .ThenBy(m => m.JoinedAt)
            .ThenBy(m => m.UserId)
            .Select(m => new MemberView(m.UserId, m.Role, m.JoinedAt))
            .Take(limit)
            .ToListAsync(ct);
    }

    public static int RankOf(GroupRole role) => role switch
    {
        GroupRole.Owner => 0,
        GroupRole.Admin => 1,
        _ => 2,
    };

    public async Task<Result<InvitationCreated>> CreateInvitationAsync(
        Guid groupId, Guid actorId, int hours, int maxUses, DateTimeOffset now, CancellationToken ct)
    {
        var actor = await GetRoleAsync(groupId, actorId, ct);
        if (actor is null)
        {
            return CommunityErrors.GroupNotFound;
        }

        if (!GroupPolicy.CanInvite(actor.Value))
        {
            return CommunityErrors.Forbidden;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var invitation = new Invitation(Guid.CreateVersion7(now), groupId, HashToken(token), actorId, now, now.AddHours(hours), maxUses);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return new InvitationCreated(invitation.Id, token, invitation.ExpiresAt, invitation.MaxUses);
    }

    /// <summary>Revogar é idempotente: revogar de novo mantém a data da primeira revogação.</summary>
    public async Task<Result> RevokeInvitationAsync(Guid groupId, Guid invitationId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var actor = await GetRoleAsync(groupId, actorId, ct);
        if (actor is null)
        {
            return CommunityErrors.GroupNotFound;
        }

        if (!GroupPolicy.CanInvite(actor.Value))
        {
            return CommunityErrors.Forbidden;
        }

        var updated = await db.Database.ExecuteSqlAsync($"""
            UPDATE communities.invitations SET revoked_at = COALESCE(revoked_at, {now})
            WHERE id = {invitationId} AND group_id = {groupId}
            """, ct);
        return updated == 1 ? Result.Success() : CommunityErrors.InvitationNotFound;
    }

    public async Task<(AcceptOutcome Outcome, Guid GroupId, GroupRole Role)> AcceptAsync(
        string token, Guid userId, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var hash = HashToken(token);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var invitation = await db.Invitations.AsNoTracking()
            .Where(i => i.TokenHash == hash)
            .Select(i => new { i.Id, i.GroupId })
            .SingleOrDefaultAsync(ct);
        if (invitation is null)
        {
            return (AcceptOutcome.Invalid, Guid.Empty, default);
        }

        // Já é membro: nada muda e nenhum uso do convite é consumido.
        if (await GetRoleAsync(invitation.GroupId, userId, ct) is { } current)
        {
            return (AcceptOutcome.AlreadyMember, invitation.GroupId, current);
        }

        var consumed = await db.Database.ExecuteSqlAsync($"""
            UPDATE communities.invitations SET use_count = use_count + 1
            WHERE id = {invitation.Id} AND revoked_at IS NULL AND expires_at > {now} AND use_count < max_uses
            """, ct);
        if (consumed == 0)
        {
            return (AcceptOutcome.Invalid, Guid.Empty, default);
        }

        var grown = await db.Database.ExecuteSqlAsync($"""
            UPDATE communities.groups SET member_count = member_count + 1
            WHERE id = {invitation.GroupId} AND member_count < {GroupRules.MaxMembers}
            """, ct);
        if (grown == 0)
        {
            await transaction.RollbackAsync(ct);
            return (AcceptOutcome.Full, invitation.GroupId, default);
        }

        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO communities.memberships (group_id, user_id, role, joined_at)
            VALUES ({invitation.GroupId}, {userId}, 'member', {now})
            ON CONFLICT (group_id, user_id) DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            // Outra requisição do mesmo usuário entrou primeiro: desfaz o uso e a contagem desta.
            await transaction.RollbackAsync(ct);
            var role = await GetRoleAsync(invitation.GroupId, userId, ct) ?? GroupRole.Member;
            return (AcceptOutcome.AlreadyMember, invitation.GroupId, role);
        }

        db.OutboxMessages.Add(OutboxMessage.From(
            new GroupMemberJoinedV1(invitation.GroupId, userId, GroupRules.ToText(GroupRole.Member)), correlationId, now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (AcceptOutcome.Joined, invitation.GroupId, GroupRole.Member);
    }

    public async Task<Result<MemberView>> ChangeRoleAsync(Guid groupId, Guid actorId, Guid targetId, GroupRole newRole, CancellationToken ct)
    {
        // Não-membro não chega ao lock: sem isso, quem só conhece o id seguraria a linha do grupo.
        if (await GetRoleAsync(groupId, actorId, ct) is null)
        {
            return CommunityErrors.GroupNotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (actor, target, error) = await LockAndLoadAsync(groupId, actorId, targetId, ct);
        if (error is not null)
        {
            return error;
        }

        if (GroupPolicy.CanChangeRole(actor!.Value, target!.Role) is { } denied)
        {
            return denied;
        }

        await db.Database.ExecuteSqlAsync($"""
            UPDATE communities.memberships SET role = {GroupRules.ToText(newRole)}
            WHERE group_id = {groupId} AND user_id = {targetId}
            """, ct);
        await transaction.CommitAsync(ct);
        return new MemberView(targetId, newRole, target.JoinedAt);
    }

    public async Task<Result> RemoveAsync(Guid groupId, Guid actorId, Guid targetId, CancellationToken ct)
    {
        if (await GetRoleAsync(groupId, actorId, ct) is null)
        {
            return CommunityErrors.GroupNotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (actor, target, error) = await LockAndLoadAsync(groupId, actorId, targetId, ct);
        if (error is not null)
        {
            return error;
        }

        if (GroupPolicy.CanRemove(actor!.Value, actorId == targetId, target!.Role) is { } denied)
        {
            return denied;
        }

        await db.Database.ExecuteSqlAsync($"""
            DELETE FROM communities.memberships WHERE group_id = {groupId} AND user_id = {targetId}
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE communities.groups SET member_count = member_count - 1 WHERE id = {groupId}
            """, ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    /// <summary>Trava o grupo e carrega os dois papéis. Quem age sem ser membro recebe <c>group-not-found</c>.</summary>
    private async Task<(GroupRole? Actor, MemberView? Target, Error? Error)> LockAndLoadAsync(
        Guid groupId, Guid actorId, Guid targetId, CancellationToken ct)
    {
        await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM communities.groups WHERE id = {groupId} FOR UPDATE
            """).ToListAsync(ct);

        var roles = await db.Memberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && (m.UserId == actorId || m.UserId == targetId))
            .Select(m => new MemberView(m.UserId, m.Role, m.JoinedAt))
            .ToListAsync(ct);

        var actor = roles.SingleOrDefault(r => r.UserId == actorId);
        if (actor is null)
        {
            return (null, null, CommunityErrors.GroupNotFound);
        }

        var target = roles.SingleOrDefault(r => r.UserId == targetId);
        return target is null
            ? (actor.Role, null, CommunityErrors.MemberNotFound)
            : (actor.Role, target, null);
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
