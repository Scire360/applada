using System.Text.Json;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Lista de participantes de uma partida (spec §7, privacidade): só o organizador e quem tem inscrição ativa
/// (<c>confirmed</c>/<c>waitlisted</c>) leem; os demais recebem <c>participants-forbidden</c>. Inscrições
/// <c>withdrawn</c> nunca aparecem. Ordem: confirmados, depois espera, cada grupo por <c>queue_seq</c>.
/// </summary>
/// <remarks>
/// Ordem das checagens: entrada (limite e cursor) → existência (<c>match-not-found</c>) → autorização. Responder 404
/// antes de 403 não vaza nada: nesta fatia toda partida é pública e <c>GET /matches/{id}</c> já mostra a qualquer
/// autenticado que ela existe. Autorização e listagem são duas leituras: quem sai entre elas ainda recebe esta página.
/// O cursor não carrega o id da partida, e isso é inofensivo: ele só posiciona a leitura (rank e <c>queue_seq</c>), e a
/// listagem é sempre filtrada pela partida da rota depois da autorização. Um cursor de outra partida no máximo pula ou
/// repete posições de uma lista que quem consulta já pode ver inteira; não dá acesso a nada além dela.
/// </remarks>
public sealed class ParticipantsQuery(IMatchQueries queries)
{
    public async Task<Result<Page<Participant>>> HandleAsync(Guid matchId, Guid viewerId, string? cursor, int limit, CancellationToken ct)
    {
        if (!PageLimit.IsValid(limit))
        {
            return PageLimit.Invalid;
        }

        ParticipantsKey? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryReadKey(cursor, out var key))
            {
                return MatchErrors.InvalidCursor;
            }

            after = key;
        }

        var viewer = await queries.GetViewerAsync(matchId, viewerId, ct);
        if (viewer is null)
        {
            return MatchErrors.MatchNotFound;
        }

        if (viewer.OrganizerId != viewerId && viewer.ActiveRegistration is null)
        {
            return MatchErrors.ParticipantsForbidden;
        }

        // Uma linha a mais só para saber se há próxima página.
        var rows = await queries.ListParticipantsAsync(matchId, after, limit + 1, ct);
        var items = rows.Take(limit).Select(r => new Participant(r.UserId, r.Status, r.Position)).ToArray();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            next = Cursor.Encode(Cursor.ParticipantsKind, ParticipantsKey.RankOf(last.Status), last.QueueSeq);
        }

        return new Page<Participant>(items, next);
    }

    /// <summary>Exatamente <c>[statusRank, queueSeq]</c>: rank 0 ou 1 e <c>queue_seq</c> inteiro positivo.</summary>
    private static bool TryReadKey(string cursor, out ParticipantsKey key)
    {
        key = null!;
        if (!Cursor.TryDecode(cursor, Cursor.ParticipantsKind, out var values)
            || values.Length != 2
            || values[0].ValueKind != JsonValueKind.Number
            || values[1].ValueKind != JsonValueKind.Number
            || !values[0].TryGetInt32(out var rank)
            || !values[1].TryGetInt64(out var queueSeq)
            || rank is not (ParticipantsKey.ConfirmedRank or ParticipantsKey.WaitlistedRank)
            || queueSeq < 1)
        {
            return false;
        }

        key = new ParticipantsKey(rank, queueSeq);
        return true;
    }
}
