using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Applada.Modules.Communities.Infrastructure;

/// <summary>Usada só pelo <c>dotnet ef</c>: monta o contexto sem banco nem configuração da API.</summary>
internal sealed class CommunitiesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CommunitiesDbContext>
{
    public CommunitiesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CommunitiesDbContext>()
            .UseNpgsql(CommunitiesDbContext.ConfigureNpgsql)
            .Options);
}
