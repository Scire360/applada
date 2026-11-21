using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Discovery.Infrastructure;

internal sealed record NearbyVenue(Guid Id, string Name, string? AddressText, double Meters);

internal sealed class NearbyVenueQuery(DiscoveryDbContext db)
{
    public Task<Dictionary<Guid, NearbyVenue>> FindAsync(double latitude, double longitude, int radiusKm, CancellationToken ct) =>
        db.Database.SqlQuery<NearbyVenue>($"""
            SELECT id, name, address_text AS "AddressText", ST_Distance(location, point) AS meters
            FROM discovery.venues
            CROSS JOIN (SELECT ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography AS point) AS origin
            WHERE ST_DWithin(location, point, {radiusKm * 1000})
            ORDER BY location <-> point, id
            LIMIT 200
            """).ToDictionaryAsync(v => v.Id, ct);
}
