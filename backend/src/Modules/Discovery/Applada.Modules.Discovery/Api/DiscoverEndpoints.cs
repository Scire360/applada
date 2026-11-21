using System.Globalization;
using Applada.Modules.Discovery.Infrastructure;
using Applada.Modules.Matches.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Discovery.Api;

public sealed record DiscoveredVenueResponse(Guid Id, string Name, string? AddressText);
public sealed record DiscoveredMatchResponse(Guid MatchId, string Title, DateTimeOffset StartsAt, string TimeZone,
    int Capacity, int ConfirmedCount, int SpotsLeft, DiscoveredVenueResponse Venue, double DistanceKm);

internal static class DiscoverEndpoints
{
    private const int DefaultRadiusKm = 10;
    private const int MaxRadiusKm = 50;

    public static IEndpointRouteBuilder MapDiscoverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DiscoveryModule.DiscoverMatchesPath, DiscoverAsync).WithTags("Discovery").WithName("DiscoverMatches")
            .RequireAuthorization()
            .Produces<Page<DiscoveredMatchResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return endpoints;
    }

    private static async Task<IResult> DiscoverAsync(
        string? lat, string? lon, string? radiusKm, string? cursor, string? limit, NearbyVenueQuery nearby,
        IOpenMatchCatalog catalog, TimeProvider clock, HttpResponse response, CancellationToken ct)
    {
        response.Headers.CacheControl = "private, no-store";
        if (!TryCoordinate(lat, 90, out var latitude))
        {
            return Invalid("lat", "Informe uma latitude entre -90 e 90.");
        }

        if (!TryCoordinate(lon, 180, out var longitude))
        {
            return Invalid("lon", "Informe uma longitude entre -180 e 180.");
        }

        if (!TryPositiveInteger(radiusKm, DefaultRadiusKm, MaxRadiusKm, out var radius))
        {
            return Invalid("radiusKm", $"O raio deve ser um inteiro entre 1 e {MaxRadiusKm}.");
        }

        if (!TryPositiveInteger(limit, MatchPageLimits.Default, MatchPageLimits.Max, out var pageSize))
        {
            return InvalidLimit();
        }

        cursor = cursor == string.Empty ? null : cursor;
        if (cursor is not null && !catalog.IsValidCursor(cursor))
        {
            return InvalidCursor();
        }

        var venues = await nearby.FindAsync(latitude, longitude, radius, ct);
        if (venues.Count == 0)
        {
            return TypedResults.Ok(new Page<DiscoveredMatchResponse>([], null));
        }

        OpenMatchPage page;
        try
        {
            page = await catalog.ListUpcomingAtVenuesAsync(venues.Keys, clock.GetUtcNow(), cursor, pageSize, ct);
        }
        catch (ArgumentException ex) when (ex.ParamName == nameof(cursor))
        {
            return InvalidCursor();
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == nameof(limit))
        {
            return InvalidLimit();
        }

        return TypedResults.Ok(new Page<DiscoveredMatchResponse>(
            [.. page.Items.Select(match => ToResponse(match, venues[match.VenueId]))], page.NextCursor));
    }

    private static DiscoveredMatchResponse ToResponse(OpenMatchSummary match, NearbyVenue venue) =>
        new(match.MatchId, match.Title, match.StartsAt, match.TimeZone, match.Capacity, match.ConfirmedCount,
            Math.Max(match.Capacity - match.ConfirmedCount, 0), new DiscoveredVenueResponse(venue.Id, venue.Name, venue.AddressText),
            Math.Round(venue.Meters / 1000, 1, MidpointRounding.AwayFromZero));

    private static bool TryCoordinate(string? input, int bound, out double rounded)
    {
        rounded = 0;
        if (!decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < -bound || value > bound)
        {
            return false;
        }

        rounded = (double)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryPositiveInteger(string? input, int defaultValue, int max, out int value)
    {
        value = defaultValue;
        return string.IsNullOrEmpty(input) || (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= max);
    }

    private static ValidationProblem InvalidCursor() => Invalid("cursor", "Cursor inválido.");

    private static ValidationProblem InvalidLimit() =>
        Invalid("limit", $"O limite deve ser um inteiro entre 1 e {MatchPageLimits.Max}.");

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] },
            title: "Dados inválidos.", type: "urn:applada:problem:validation");
}
