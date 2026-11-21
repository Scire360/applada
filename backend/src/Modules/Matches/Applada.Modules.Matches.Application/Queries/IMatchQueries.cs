using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Leituras de Matches sem lock e sem rastreamento. As listagens são por chave (keyset, nunca OFFSET): devolvem até
/// <c>take</c> linhas estritamente depois de <c>after</c>, na ordem da listagem.
/// </summary>
public interface IMatchQueries
{
    /// <summary>A partida vista por <paramref name="viewerId"/>, ou <c>null</c> se não existir.</summary>
    Task<MatchDetails?> GetAsync(Guid matchId, Guid viewerId, CancellationToken ct);

    /// <summary>
    /// Autoriza <paramref name="viewerId"/> e lê a página no mesmo snapshot. Devolve erro se a partida não existir
    /// ou se o usuário não for o organizador nem tiver inscrição ativa. Ordena confirmados e depois espera por <c>queue_seq</c>.
    /// </summary>
    Task<Result<IReadOnlyList<ParticipantRow>>> ListParticipantsAsync(Guid matchId, Guid viewerId, ParticipantsKey? after, int take, CancellationToken ct);

    Task<Result<IReadOnlyList<AttendanceRow>>> ListAttendanceAsync(Guid matchId, Guid viewerId, ParticipantsKey? after, int take, CancellationToken ct);

    /// <summary>Partidas organizadas por <paramref name="userId"/> com <c>starts_at &gt;= earliestStart</c>, por <c>(starts_at, id)</c>.</summary>
    Task<IReadOnlyList<MyMatchRow>> ListOrganizedAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct);

    /// <summary>
    /// Partidas agendadas do grupo <paramref name="groupId"/> com <c>starts_at &gt;= earliestStart</c>, por <c>(starts_at, id)</c>;
    /// <c>MyStatus</c> é a inscrição ativa de <paramref name="userId"/>. Não verifica a associação ao grupo.
    /// </summary>
    Task<IReadOnlyList<MyMatchRow>> ListGroupUpcomingAsync(Guid groupId, Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct);

    /// <summary>
    /// Partidas com inscrição ativa de <paramref name="userId"/> e <c>starts_at &gt;= earliestStart</c>, por <c>(starts_at, id)</c>.
    /// </summary>
    Task<IReadOnlyList<MyMatchRow>> ListParticipatingAsync(Guid userId, DateTimeOffset earliestStart, MyMatchesKey? after, int take, CancellationToken ct);
}
