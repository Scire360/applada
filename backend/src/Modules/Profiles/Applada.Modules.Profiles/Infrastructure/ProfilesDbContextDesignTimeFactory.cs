using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Applada.Modules.Profiles.Infrastructure;

internal sealed class ProfilesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ProfilesDbContext>
{
    public ProfilesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProfilesDbContext>()
            .UseNpgsql(ProfilesDbContext.ConfigureNpgsql)
            .Options);
}
