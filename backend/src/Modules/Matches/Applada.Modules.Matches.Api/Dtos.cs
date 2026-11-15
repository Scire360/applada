using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// Corpo de <c>POST /api/v1/matches</c>. Campos ausentes chegam nulos ou zerados e são recusados pela validação do
/// domínio (400 com <c>errors</c> por campo).
/// </summary>
public sealed record CreateMatchRequest(
    string Title,
    DateTimeOffset StartsAt,
    string TimeZone,
    int DurationMinutes,
    int Capacity,
    string Visibility,
    string? LocationText,
    Guid? GroupId,
    Guid? VenueId)
{
    internal NewMatch ToNewMatch() =>
        new(Title, StartsAt, TimeZone, DurationMinutes, Capacity, Visibility, LocationText, GroupId, VenueId);
}

/// <summary>
/// Corpo de <c>PATCH /api/v1/matches/{id}</c>. Campo nulo ou ausente não altera; <c>locationText</c> vazio limpa o local.
/// </summary>
public sealed record UpdateMatchRequest(string? Title, string? LocationText, int? Capacity);

public sealed record MatchResponse(
    Guid Id,
    Guid OrganizerId,
    string Title,
    DateTimeOffset StartsAt,
    string TimeZone,
    int DurationMinutes,
    int Capacity,
    string Visibility,
    string Status,
    int ConfirmedCount,
    int WaitlistCount,
    string? LocationText,
    Guid? GroupId,
    Guid? VenueId,
    int Version,
    MyRegistrationResponse? MyRegistration)
{
    internal static MatchResponse From(MatchDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return new MatchResponse(
            details.Id,
            details.OrganizerId,
            details.Title,
            details.StartsAt,
            details.TimeZone,
            details.DurationMinutes,
            details.Capacity,
            Text(details.Visibility),
            Text(details.Status),
            details.ConfirmedCount,
            details.WaitlistCount,
            details.LocationText,
            details.GroupId,
            details.VenueId,
            details.Version,
            details.MyRegistration is { } mine ? new MyRegistrationResponse(Text(mine.Status), mine.Position) : null);
    }

    // Textos do contrato HTTP fixados aqui (não ToString do enum): renomear o enum não muda a API.
    private static string Text(MatchVisibility value) => value switch
    {
        MatchVisibility.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Visibilidade sem texto na API."),
    };

    private static string Text(MatchStatus value) => value switch
    {
        MatchStatus.Scheduled => "scheduled",
        MatchStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Status sem texto na API."),
    };

    private static string Text(RegistrationStatus value) => value switch
    {
        RegistrationStatus.Confirmed => "confirmed",
        RegistrationStatus.Waitlisted => "waitlisted",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Inscrição inativa não aparece na API."),
    };
}

/// <summary>Inscrição ativa de quem consulta; <see cref="Position"/> só em espera (1 = próximo a ser promovido).</summary>
public sealed record MyRegistrationResponse(string Status, int? Position);
