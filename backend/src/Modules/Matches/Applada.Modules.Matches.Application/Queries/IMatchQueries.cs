namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Leituras de Matches sem lock e sem rastreamento. As listagens são por chave (keyset, nunca OFFSET): devolvem até
/// <c>take</c> linhas estritamente depois de <c>after</c>, na ordem da listagem.
/// </summary>
public interface IMatchQueries
{
    /// <summary>A partida vista por <paramref name="viewerId"/>, ou <c>null</c> se não existir.</summary>
    Task<MatchDetails?> GetAsync(Guid matchId, Guid viewerId, CancellationToken ct);

    /// <summary>Organizador e inscrição ativa de <paramref name="viewerId"/> numa leitura só; <c>null</c> se a partida não existir.</summary>
    Task<MatchViewer?> GetViewerAsync(Guid matchId, Guid viewerId, CancellationToken ct);

    /// <summary>Inscrições ativas: confirmados e depois espera, cada grupo por <c>queue_seq</c>.</summary>
    Task<IReadOnlyList<ParticipantRow>> ListParticipantsAsync(Guid matchId, ParticipantsKey? after, int take, CancellationToken ct);

    /// <summary>Partidas organizadas por <paramref name="userId"/> com <c>starts_at &gt;= earliestStart</c>, por <c>(starts_at, id)</c>.</summary>
    Task<IReadOnlyList<MyMatchRow>> ListOrganizedAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct);

    /// <summary>
    /// Partidas com inscrição ativa de <paramref name="userId"/> e <c>starts_at &gt;= earliestStart</c>, por <c>(starts_at, id)</c>.
    /// </summary>
    Task<IReadOnlyList<MyMatchRow>> ListParticipatingAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct);
}
