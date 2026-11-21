using System.Diagnostics.Metrics;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Domain.Events;

namespace Applada.Modules.Matches.Application;

/// <summary>
/// Instrumentos do meter <c>Applada.Matches</c>:
/// <list type="bullet">
/// <item><c>matches.registrations</c>, contador com a tag <c>result</c> (<c>confirmed</c>, <c>waitlisted</c>,
/// <c>unchanged</c>, <c>withdrawn</c>, <c>promoted</c>, <c>rejected</c>);</item>
/// <item><c>matches.lock_timeouts</c>, contador de transações que não obtiveram o lock da partida no prazo.</item>
/// </list>
/// Os handlers registram só depois do commit: inscrição desfeita (erro, lock-timeout ou exceção) nunca conta como
/// confirmada. <c>rejected</c> é inscrição ou saída recusada por regra (partida inexistente, cancelada, fechada, espera
/// cheia, sem inscrição ativa); lock-timeout conta só em <c>matches.lock_timeouts</c>.
/// </summary>
public sealed class MatchesMetrics
{
    public const string MeterName = "Applada.Matches";

    private const string ResultTag = "result";

    private readonly Counter<long> _registrations;
    private readonly Counter<long> _lockTimeouts;

    public MatchesMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _registrations = meter.CreateCounter<long>("matches.registrations", description: "Inscrições, saídas e promoções confirmadas, por resultado.");
        _lockTimeouts = meter.CreateCounter<long>("matches.lock_timeouts", description: "Escritas que não obtiveram o lock da partida dentro do lock_timeout.");
    }

    /// <summary>Inscrição gravada: <c>confirmed</c> ou <c>waitlisted</c>; <c>unchanged</c> quando já estava ativa.</summary>
    public void Registered(RegistrationStatus status, bool changed)
    {
        var result = !changed
            ? "unchanged"
            : status switch
            {
                RegistrationStatus.Confirmed => "confirmed",
                RegistrationStatus.Waitlisted => "waitlisted",
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Inscrição gravada deve estar ativa."),
            };
        Add(result);
    }

    public void Withdrawn() => Add("withdrawn");

    /// <summary>Promoções da espera confirmadas numa transação (saída de confirmado ou aumento de capacidade).</summary>
    public void Promoted(int count)
    {
        if (count > 0)
        {
            _registrations.Add(count, new KeyValuePair<string, object?>(ResultTag, "promoted"));
        }
    }

    /// <summary>Inscrição ou saída recusada. Lock-timeout não passa por aqui (é contado pela unidade de trabalho).</summary>
    public void Rejected(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code != MatchErrors.LockTimeout.Code)
        {
            Add("rejected");
        }
    }

    public void LockTimedOut() => _lockTimeouts.Add(1);

    /// <summary>Quantas promoções da espera há nos eventos pendentes da partida (antes de irem para o outbox).</summary>
    public static int PendingPromotions(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.DomainEvents.OfType<SpotConfirmed>().Count(e => e.Source == ConfirmationSource.WaitlistPromotion);
    }

    private void Add(string result) => _registrations.Add(1, new KeyValuePair<string, object?>(ResultTag, result));
}
