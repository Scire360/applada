namespace Applada.Modules.Matches.Domain;

/// <summary>Dados de criação de partida, ainda não validados (validação em <see cref="Match.Create"/>).</summary>
public sealed record NewMatch(
    string Title,
    DateTimeOffset StartsAt,
    string TimeZone,
    int DurationMinutes,
    int Capacity,
    string Visibility,
    string? LocationText,
    Guid? GroupId,
    Guid? VenueId);
