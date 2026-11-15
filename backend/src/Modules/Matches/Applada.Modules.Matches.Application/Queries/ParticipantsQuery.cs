using System.Text.Json;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Profiles.Contracts;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Lista de participantes de uma partida (spec §7, privacidade): só o organizador e quem tem inscrição ativa
/// (<c>confirmed</c>/<c>waitlisted</c>) leem; os demais recebem <c>participants-forbidden</c>. Inscrições
/// <c>withdrawn</c> nunca aparecem. Ordem: confirmados, depois espera, cada grupo por <c>queue_seq</c>.
/// </summary>
/// <remarks>
/// Ordem das checagens: entrada (limite e cursor) → existência (<c>match-not-found</c>) → autorização. Responder 404
/// antes de 403 não vaza nada: nesta fatia toda partida é pública e <c>GET /matches/{id}</c> já mostra a qualquer
/// autenticado que ela existe. A autorização e a página usam o mesmo snapshot do banco.
/// O cursor só posiciona a leitura (rank e <c>queue_seq</c>) entre as inscrições da partida da rota.
/// </remarks>
public sealed class ParticipantsQuery(IMatchQueries queries, IProfileNames profiles)
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

        // Uma linha a mais só para saber se há próxima página.
        var result = await queries.ListParticipantsAsync(matchId, viewerId, after, limit + 1, ct);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var rows = result.Value;
        var page = rows.Take(limit).ToArray();
        if (page.Length == 0)
        {
            return new Page<Participant>([], null);
        }

        // Buscar nomes apenas depois da autorização e somente para os usuários desta página.
        var names = await profiles.ListAsync(page.Select(p => p.UserId).ToArray(), ct);
        var items = page.Select(p => new Participant(p.UserId, p.Status, p.Position, names.GetValueOrDefault(p.UserId))).ToArray();
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
