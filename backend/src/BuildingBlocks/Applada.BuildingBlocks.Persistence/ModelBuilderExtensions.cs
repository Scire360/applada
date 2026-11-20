using Applada.BuildingBlocks.Persistence.Naming;
using Microsoft.EntityFrameworkCore;

namespace Applada.BuildingBlocks.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Aplica snake_case a tabelas, colunas, chaves, FKs e índices gerados por convenção.
    /// Chamar em <c>ConfigureConventions</c> do <c>DbContext</c>.
    /// </summary>
    public static void UseSnakeCaseNames(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
    }
}
