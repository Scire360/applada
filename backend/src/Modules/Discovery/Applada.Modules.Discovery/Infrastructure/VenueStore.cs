using Applada.Modules.Discovery.Contracts;
using Applada.Modules.Discovery.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Discovery.Infrastructure;

internal sealed class VenueStore(DiscoveryDbContext db) : IVenueDirectory
{
    public Task<bool> ExistsAsync(Guid venueId, CancellationToken ct) =>
        db.Venues.AnyAsync(v => v.Id == venueId, ct);

    public async Task AddAsync(Venue venue, CancellationToken ct)
    {
        db.Venues.Add(venue);
        await db.SaveChangesAsync(ct);
    }

    public Task<Venue?> FindAsync(Guid id, CancellationToken ct) =>
        db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);

    public Task<List<Venue>> ListMineAsync(
        Guid owner, (DateTimeOffset CreatedAt, Guid Id)? after, int limit, CancellationToken ct)
    {
        var query = db.Venues.AsNoTracking().Where(v => v.CreatedBy == owner);
        if (after is { } key)
        {
            query = query.Where(v => EF.Functions.LessThan(
                ValueTuple.Create(v.CreatedAt, v.Id), ValueTuple.Create(key.CreatedAt, key.Id)));
        }

        return query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Take(limit + 1).ToListAsync(ct);
    }
}
