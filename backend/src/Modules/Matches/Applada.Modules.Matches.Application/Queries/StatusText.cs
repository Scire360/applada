using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Textos de status do contrato HTTP, fixados aqui (não <c>ToString</c> do enum): renomear um membro do enum não muda a
/// API. Inscrição <see cref="RegistrationStatus.Withdrawn"/> nunca é exibida.
/// </summary>
public static class StatusText
{
    public static string Of(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Confirmed => "confirmed",
        RegistrationStatus.Waitlisted => "waitlisted",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Inscrição inativa não aparece na API."),
    };

    public static string Of(MatchStatus status) => status switch
    {
        MatchStatus.Scheduled => "scheduled",
        MatchStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status sem texto na API."),
    };
}
