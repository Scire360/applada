using System.Text.Json;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Participante exibido na lista. <see cref="Position"/> só em espera: quantos <c>waitlisted</c> têm <c>queue_seq</c>
/// menor ou igual (1 = próximo a ser promovido).
/// </summary>
public sealed record Participant(Guid UserId, RegistrationStatus Status, int? Position, string? DisplayName);

/// <summary>Linha da listagem de participantes, com a posição de fila que vira cursor.</summary>
public sealed record ParticipantRow(Guid UserId, RegistrationStatus Status, long QueueSeq, int? Position);

/// <summary>Chave da listagem de participantes: confirmados (0) antes da espera (1), depois <c>queue_seq</c>.</summary>
public sealed record ParticipantsKey(int StatusRank, long QueueSeq)
{
    public const int ConfirmedRank = 0;
    public const int WaitlistedRank = 1;

    /// <summary>Exatamente <c>[statusRank, queueSeq]</c>: rank 0 ou 1 e <c>queue_seq</c> inteiro positivo.</summary>
    internal static bool TryRead(string cursor, out ParticipantsKey key)
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

    public static int RankOf(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Confirmed => ConfirmedRank,
        RegistrationStatus.Waitlisted => WaitlistedRank,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Inscrição inativa não é listada."),
    };
}

/// <summary>Chave de "minhas partidas": <c>(starts_at, id)</c>, com <see cref="StartsAt"/> em UTC.</summary>
public sealed record MyMatchesKey(DateTimeOffset StartsAt, Guid Id);

/// <summary>Linha de "minhas partidas" como está no banco.</summary>
public sealed record MyMatchRow(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    MatchStatus Status,
    int ConfirmedCount,
    int Capacity,
    RegistrationStatus? MyStatus);

/// <summary>
/// Item de <c>GET /me/matches</c>. <see cref="MyStatus"/> é a inscrição ativa de quem consulta (<c>confirmed</c> ou
/// <c>waitlisted</c>), nula quando ele só organiza.
/// </summary>
public sealed record MyMatchSummary(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    string Status,
    int ConfirmedCount,
    int Capacity,
    string? MyStatus);
