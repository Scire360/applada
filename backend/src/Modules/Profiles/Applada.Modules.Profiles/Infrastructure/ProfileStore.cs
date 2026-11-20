using Applada.Modules.Profiles.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Profiles.Infrastructure;

internal sealed class ProfileStore(ProfilesDbContext db) : IProfileNames
{
    public Task<string?> FindAsync(Guid userId, CancellationToken ct) =>
        db.PlayerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => profile.DisplayName)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        await db.PlayerProfiles.AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, profile => profile.DisplayName, ct);

    public Task SaveAsync(Guid userId, string displayName, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO profiles.player_profiles (user_id, display_name)
            VALUES ({userId}, {displayName})
            ON CONFLICT (user_id) DO UPDATE SET display_name = EXCLUDED.display_name
            """, ct);
}
