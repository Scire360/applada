using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Applada.Modules.Discovery.Infrastructure;

internal sealed class DiscoveryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DiscoveryDbContext>
{
    public DiscoveryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DiscoveryDbContext>().UseNpgsql(DiscoveryDbContext.ConfigureNpgsql).Options);
}
