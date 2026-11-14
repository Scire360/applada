using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Applada.Modules.Identity.Infrastructure;

/// <summary>
/// Usada só pelo <c>dotnet ef</c>: monta o contexto sem banco nem configuração da API.
/// </summary>
internal sealed class IdentityDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(IdentityDbContext.ConfigureNpgsql)
            .Options);
}
