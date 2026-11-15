using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Events;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.CancelMatch;

/// <summary>Cancelamento pelo organizador, sob o lock da partida. As inscrições ficam como histórico.</summary>
public sealed class CancelMatchHandler(IMatchesUnitOfWork unitOfWork, IMatchQueries queries, TimeProvider timeProvider)
{
    public async Task<Result<MatchDetails>> HandleAsync(Guid matchId, Guid userId, CancellationToken ct)
    {
        var cancelled = await unitOfWork.ExecuteAsync<bool>(
            async (session, token) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, token);
                if (match is null)
                {
                    return MatchErrors.MatchNotFound;
                }

                var result = match.Cancel(userId, timeProvider.GetUtcNow());
                if (!result.IsSuccess)
                {
                    return result.Error;
                }

                MatchEventMapper.PublishPending(match, session);
                return true;
            },
            ct);

        if (!cancelled.IsSuccess)
        {
            return cancelled.Error;
        }

        return await queries.GetAsync(matchId, userId, ct) is { } details ? details : MatchErrors.MatchNotFound;
    }
}
