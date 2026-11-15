namespace Applada.Modules.Matches.Application.Queries;

/// <summary>Leituras de Matches sem lock e sem rastreamento.</summary>
public interface IMatchQueries
{
    /// <summary>A partida vista por <paramref name="viewerId"/>, ou <c>null</c> se não existir.</summary>
    Task<MatchDetails?> GetAsync(Guid matchId, Guid viewerId, CancellationToken ct);
}
