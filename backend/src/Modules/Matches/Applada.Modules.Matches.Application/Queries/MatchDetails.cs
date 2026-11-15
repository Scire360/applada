using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Partida vista por um usuário. <see cref="MyRegistration"/> é a inscrição ativa de quem consulta (nula se não houver).
/// </summary>
public sealed record MatchDetails(
    Guid Id,
    Guid OrganizerId,
    string Title,
    DateTimeOffset StartsAt,
    string TimeZone,
    int DurationMinutes,
    int Capacity,
    MatchVisibility Visibility,
    MatchStatus Status,
    int ConfirmedCount,
    int WaitlistCount,
    string? LocationText,
    Guid? GroupId,
    Guid? VenueId,
    int Version,
    MyRegistration? MyRegistration)
{
    /// <summary>Detalhes a partir do agregado (ex.: logo após a criação, quando ninguém está inscrito).</summary>
    public static MatchDetails From(Match match, MyRegistration? myRegistration)
    {
        ArgumentNullException.ThrowIfNull(match);

        return new MatchDetails(
            match.Id,
            match.OrganizerId,
            match.Title,
            match.StartsAt,
            match.TimeZone,
            match.DurationMinutes,
            match.Capacity,
            match.Visibility,
            match.Status,
            match.ConfirmedCount,
            match.WaitlistCount,
            match.LocationText,
            match.GroupId,
            match.VenueId,
            match.Version,
            myRegistration);
    }
}

/// <summary>
/// Inscrição ativa de quem consulta. <see cref="Position"/> só existe em espera: quantos <c>waitlisted</c> têm
/// <c>queue_seq</c> menor ou igual ao do usuário (1 = próximo a ser promovido).
/// </summary>
public sealed record MyRegistration(RegistrationStatus Status, int? Position);
