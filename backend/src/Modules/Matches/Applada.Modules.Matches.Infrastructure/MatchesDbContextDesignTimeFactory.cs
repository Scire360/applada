using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Usada só pelo <c>dotnet ef</c>: monta o contexto sem banco nem configuração da API.
/// </summary>
internal sealed class MatchesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MatchesDbContext>
{
    public MatchesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MatchesDbContext>()
            .UseNpgsql(MatchesDbContext.ConfigureNpgsql)
            .Options);
}
