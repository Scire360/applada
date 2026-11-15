using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Leituras sem lock e <c>AsNoTracking</c>: não interferem na identity map de uma unidade de trabalho no mesmo escopo.
/// As consultas não estão numa transação; uma escrita entre elas pode deixar a posição desatualizada por um instante.
/// As listagens são uma instrução SQL cada (sem N+1), parametrizadas pelo LINQ e paginadas por chave (sem OFFSET):
/// <list type="bullet">
/// <item>participantes: índice <c>(match_id, status, queue_seq)</c>; a posição na espera é uma subconsulta no mesmo
/// índice, na mesma instrução (mesmo snapshot);</item>
/// <item>organizador: índice <c>(organizer_id, starts_at)</c>;</item>
/// <item>participante: índice <c>(user_id, status)</c>, com junção pela chave primária da partida.</item>
/// </list>
/// </summary>
internal sealed class MatchQueries(MatchesDbContext db) : IMatchQueries
{
    public async Task<MatchDetails?> GetAsync(Guid matchId, Guid viewerId, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null)
        {
            return null;
        }

        var registration = await db.Registrations.AsNoTracking()
            .Where(r => r.MatchId == matchId
                && r.UserId == viewerId
                && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted))
            .Select(r => new { r.Status, r.QueueSeq })
            .SingleOrDefaultAsync(ct);

        MyRegistration? mine = null;
        if (registration is not null)
        {
            int? position = null;
            if (registration.Status == RegistrationStatus.Waitlisted)
            {
                position = await db.Registrations.CountAsync(
                    r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted && r.QueueSeq <= registration.QueueSeq,
                    ct);
            }

            mine = new MyRegistration(registration.Status, position);
        }

        return MatchDetails.From(match, mine);
    }

    public Task<MatchViewer?> GetViewerAsync(Guid matchId, Guid viewerId, CancellationToken ct) =>
        db.Matches.AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => new MatchViewer(
                m.OrganizerId,
                db.Registrations
                    .Where(r => r.MatchId == m.Id
                        && r.UserId == viewerId
                        && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted))
                    .Select(r => (RegistrationStatus?)r.Status)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ParticipantRow>> ListParticipantsAsync(Guid matchId, ParticipantsKey? after, int take, CancellationToken ct)
    {
        var active = db.Registrations.AsNoTracking()
            .Where(r => r.MatchId == matchId
                && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted));

        // (rank, queue_seq) > (after.rank, after.queue_seq), com rank 0 = confirmado e 1 = espera.
        if (after is { StatusRank: ParticipantsKey.ConfirmedRank } confirmedKey)
        {
            active = active.Where(r => r.Status == RegistrationStatus.Waitlisted || r.QueueSeq > confirmedKey.QueueSeq);
        }
        else if (after is { } waitlistedKey)
        {
            active = active.Where(r => r.Status == RegistrationStatus.Waitlisted && r.QueueSeq > waitlistedKey.QueueSeq);
        }

        return await active
            .OrderBy(r => r.Status == RegistrationStatus.Confirmed ? ParticipantsKey.ConfirmedRank : ParticipantsKey.WaitlistedRank)
            .ThenBy(r => r.QueueSeq)
            .Take(take)
            .Select(r => new ParticipantRow(
                r.UserId,
                r.Status,
                r.QueueSeq,
                r.Status == RegistrationStatus.Waitlisted
                    ? db.Registrations.Count(w => w.MatchId == matchId && w.Status == RegistrationStatus.Waitlisted && w.QueueSeq <= r.QueueSeq)
                    : null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MyMatchRow>> ListOrganizedAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct)
    {
        var matches = db.Matches.AsNoTracking().Where(m => m.OrganizerId == userId && m.StartsAt >= earliestStart);
        if (after is { } key)
        {
            matches = matches.Where(m => EF.Functions.GreaterThan(ValueTuple.Create(m.StartsAt, m.Id), ValueTuple.Create(key.StartsAt, key.Id)));
        }

        return await matches
            .OrderBy(m => m.StartsAt)
            .ThenBy(m => m.Id)
            .Take(take)
            .Select(m => new MyMatchRow(
                m.Id,
                m.Title,
                m.StartsAt,
                m.Status,
                m.ConfirmedCount,
                m.Capacity,
                db.Registrations
                    .Where(r => r.MatchId == m.Id
                        && r.UserId == userId
                        && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted))
                    .Select(r => (RegistrationStatus?)r.Status)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MyMatchRow>> ListParticipatingAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct)
    {
        var rows =
            from r in db.Registrations.AsNoTracking()
            join m in db.Matches.AsNoTracking() on r.MatchId equals m.Id
            where r.UserId == userId
                && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted)
                && m.StartsAt >= earliestStart
            select new { Match = m, r.Status };
        if (after is { } key)
        {
            rows = rows.Where(x => EF.Functions.GreaterThan(ValueTuple.Create(x.Match.StartsAt, x.Match.Id), ValueTuple.Create(key.StartsAt, key.Id)));
        }

        return await rows
            .OrderBy(x => x.Match.StartsAt)
            .ThenBy(x => x.Match.Id)
            .Take(take)
            .Select(x => new MyMatchRow(
                x.Match.Id,
                x.Match.Title,
                x.Match.StartsAt,
                x.Match.Status,
                x.Match.ConfirmedCount,
                x.Match.Capacity,
                (RegistrationStatus?)x.Status))
            .ToListAsync(ct);
    }
}
