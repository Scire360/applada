namespace Applada.Modules.Matches.Contracts;

public interface IOpenMatchCatalog
{
    /// <summary>Valida o cursor desta listagem sem consultar o banco.</summary>
    bool IsValidCursor(string cursor);

    /// <summary>Partidas públicas agendadas após <paramref name="now"/>, por início e id, inclusive as lotadas.</summary>
    /// <exception cref="ArgumentException">Cursor inválido para esta listagem.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Limite fora de 1 a <see cref="MatchPageLimits.Max"/>.</exception>
    Task<OpenMatchPage> ListUpcomingAtVenuesAsync(
        IReadOnlyCollection<Guid> venueIds, DateTimeOffset now, string? cursor, int limit, CancellationToken ct);
}

public static class MatchPageLimits
{
    public const int Default = 20;
    public const int Max = 50;
}

public sealed record OpenMatchPage(IReadOnlyList<OpenMatchSummary> Items, string? NextCursor);

public sealed record OpenMatchSummary(
    Guid MatchId, Guid VenueId, string Title, DateTimeOffset StartsAt, string TimeZone, int Capacity, int ConfirmedCount);
