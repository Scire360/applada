using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Applada.BuildingBlocks.Persistence.Naming;

/// <summary>
/// Converte para snake_case os nomes de tabela, coluna, chave, FK e índice que o EF Core
/// gera por convenção. Nomes configurados explicitamente (<c>ToTable</c>, <c>HasColumnName</c>,
/// <c>HasName</c>, <c>HasConstraintName</c>, <c>HasDatabaseName</c> ou data annotations) são mantidos:
/// só reescrevemos quando a origem da configuração é nula (nome padrão) ou <see cref="ConfigurationSource.Convention"/>.
/// </summary>
internal sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();

        // Tabelas e colunas primeiro: nomes padrão de chave, FK e índice derivam deles.
        foreach (var entityType in entityTypes)
        {
            RenameTable(entityType);
        }

        foreach (var entityType in entityTypes)
        {
            RenameColumns(entityType);
        }

        foreach (var entityType in entityTypes)
        {
            RenameConstraints(entityType);
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var hasPrevious = i > 0 && name[i - 1] != '_';
                var previousIsLowerOrDigit = hasPrevious && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var endsAcronym = hasPrevious
                    && char.IsUpper(name[i - 1])
                    && i + 1 < name.Length
                    && char.IsLower(name[i + 1]);

                if (previousIsLowerOrDigit || endsAcronym)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static bool IsOverridable(ConfigurationSource? source) =>
        source is null or ConfigurationSource.Convention;

    private static void RenameTable(IConventionEntityType entityType)
    {
        // Tipos derivados em TPH e owned types em table splitting herdam a tabela do tipo raiz/dono,
        // que é renomeada (ou preservada, se explícita) na própria vez dele. Derivados em TPT/TPC
        // têm tabela própria e seguem o fluxo normal.
        if (SharesBaseTable(entityType) || SharesOwnerTable(entityType))
        {
            return;
        }

        var tableName = entityType.GetTableName();
        if (tableName is null || !IsOverridable(entityType.GetTableNameConfigurationSource()))
        {
            return;
        }

        entityType.Builder.ToTable(ToSnakeCase(tableName));
    }

    private static bool SharesBaseTable(IConventionEntityType entityType) =>
        entityType.BaseType is { } baseType && SameTable(entityType, baseType);

    private static bool SharesOwnerTable(IConventionEntityType entityType) =>
        entityType.FindOwnership()?.PrincipalEntityType is { } owner && SameTable(entityType, owner);

    private static bool SameTable(IConventionEntityType first, IConventionEntityType second) =>
        first.GetTableName() == second.GetTableName() && first.GetSchema() == second.GetSchema();

    private static void RenameColumns(IConventionEntityType entityType)
    {
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
        if (table is null)
        {
            return;
        }

        foreach (var property in entityType.GetDeclaredProperties())
        {
            var columnName = property.GetColumnName(table.Value);
            if (columnName is null || !IsOverridable(property.GetColumnNameConfigurationSource()))
            {
                continue;
            }

            property.Builder.HasColumnName(ToSnakeCase(columnName));
        }
    }

    private static void RenameConstraints(IConventionEntityType entityType)
    {
        if (entityType.GetTableName() is null)
        {
            return;
        }

        foreach (var key in entityType.GetDeclaredKeys())
        {
            if (IsOverridable(key.GetNameConfigurationSource()) && key.GetName() is { } name)
            {
                key.Builder.HasName(ToSnakeCase(name));
            }
        }

        foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
        {
            if (IsOverridable(foreignKey.GetConstraintNameConfigurationSource())
                && foreignKey.GetConstraintName() is { } name)
            {
                foreignKey.Builder.HasConstraintName(ToSnakeCase(name));
            }
        }

        foreach (var index in entityType.GetDeclaredIndexes())
        {
            if (IsOverridable(index.GetDatabaseNameConfigurationSource())
                && index.GetDatabaseName() is { } name)
            {
                index.Builder.HasDatabaseName(ToSnakeCase(name));
            }
        }
    }
}
