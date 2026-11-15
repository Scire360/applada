using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Leituras sem lock e <c>AsNoTracking</c>: não interferem na identity map de uma unidade de trabalho no mesmo escopo.
/// As consultas não estão numa transação; uma escrita entre elas pode deixar a posição desatualizada por um instante.
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
}
