using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Ports;

public sealed record AttendanceSubject(Match Match, Registration? Registration);

/// <summary>
/// Operações disponíveis dentro de <see cref="IMatchesUnitOfWork.ExecuteAsync{T}"/>. Válida só durante a chamada.
/// Inscrições só são escritas sob o lock da partida: chame <see cref="GetMatchForUpdateAsync"/> antes de ler
/// inscrições que serão alteradas.
/// </summary>
public interface IMatchesSession
{
    /// <summary>Carrega a partida com <c>SELECT ... FOR UPDATE</c>; o lock vale até o fim da transação.</summary>
    Task<Match?> GetMatchForUpdateAsync(Guid matchId, CancellationToken ct);

    Task<AttendanceSubject?> GetAttendanceSubjectAsync(Guid matchId, Guid userId, CancellationToken ct);

    /// <summary>Preserva a data original se o status for repetido e devolve o instante efetivamente persistido.</summary>
    Task<(DateTimeOffset RecordedAt, bool Changed)> UpsertAttendanceAsync(
        Guid matchId, Guid userId, string status, Guid recordedBy, DateTimeOffset recordedAt, CancellationToken ct);

    Task<Registration?> FindRegistrationAsync(Guid matchId, Guid userId, CancellationToken ct);

    /// <summary>Até <paramref name="count"/> inscrições em espera, em ordem de fila. <paramref name="count"/> &lt;= 0 devolve vazio.</summary>
    Task<IReadOnlyList<Registration>> GetWaitlistHeadAsync(Guid matchId, int count, CancellationToken ct);

    /// <summary>
    /// Quantas inscrições em espera gravadas têm <c>queue_seq</c> menor ou igual a <paramref name="queueSeq"/> (a posição
    /// de quem tem esse <c>queue_seq</c>). Lê o banco: alterações ainda não gravadas nesta sessão não contam.
    /// </summary>
    Task<int> CountWaitlistedUpToAsync(Guid matchId, long queueSeq, CancellationToken ct);

    /// <summary>
    /// Resposta guardada para a chave do usuário. Entrada com mais de 24 h (pelo <see cref="TimeProvider"/>) conta como
    /// ausente e é removida nesta transação, para que a chave possa ser gravada de novo com <see cref="AddIdempotency"/>.
    /// </summary>
    Task<IdempotencyEntry?> FindIdempotencyAsync(Guid userId, string key, CancellationToken ct);

    void AddMatch(Match match);

    void AddRegistration(Registration registration);

    void AddIdempotency(IdempotencyEntry entry);

    /// <summary>Grava o evento no outbox do módulo, na mesma transação.</summary>
    void Publish(IntegrationEvent evt);
}
