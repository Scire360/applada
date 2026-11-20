namespace Applada.Modules.Matches.Domain;

/// <summary>
/// Inscrição de um usuário numa partida. Só o <see cref="Match"/> cria ou altera inscrições: é ele quem
/// mantém os contadores e a fila coerentes. A mesma linha é reaproveitada quando o usuário se reinscreve.
/// </summary>
public sealed class Registration
{
    internal Registration(Guid id, Guid matchId, Guid userId, RegistrationStatus status, long queueSeq, DateTimeOffset now)
    {
        Id = id;
        MatchId = matchId;
        UserId = userId;
        Status = status;
        QueueSeq = queueSeq;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // Materialização pelo EF Core.
    private Registration()
    {
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public Guid UserId { get; private set; }

    public RegistrationStatus Status { get; private set; }

    /// <summary>Posição na ordem de chegada (FIFO), atribuída pelo <see cref="Match"/> sob o lock da partida.</summary>
    public long QueueSeq { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal bool IsActive => Status is RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted;

    /// <summary>Reinscrição: volta a ficar ativa com nova posição na fila (fim da fila).</summary>
    internal void Reenter(RegistrationStatus status, long queueSeq, DateTimeOffset now)
    {
        Status = status;
        QueueSeq = queueSeq;
        UpdatedAt = now;
    }

    internal void Confirm(DateTimeOffset now)
    {
        Status = RegistrationStatus.Confirmed;
        UpdatedAt = now;
    }

    internal void Withdraw(DateTimeOffset now)
    {
        Status = RegistrationStatus.Withdrawn;
        UpdatedAt = now;
    }
}
