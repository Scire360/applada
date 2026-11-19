using System.Xml.Linq;

namespace Applada.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void Api_DeveDelegarPostgresAoBuildingBlockPersistence()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiProjectPath = Path.Combine(
            repositoryRoot,
            "backend",
            "src",
            "Applada.Api",
            "Applada.Api.csproj");
        var apiProject = XDocument.Load(apiProjectPath);

        var directPostgresDependency = apiProject
            .Descendants("PackageReference")
            .Any(element =>
                string.Equals(
                    element.Attribute("Include")?.Value,
                    "Npgsql",
                    StringComparison.OrdinalIgnoreCase));

        Assert.False(directPostgresDependency);

        var persistenceReference = apiProject
            .Descendants("ProjectReference")
            .Any(element =>
            {
                var include = element.Attribute("Include")?.Value;
                if (include is null)
                {
                    return false;
                }

                var normalizedInclude = include.Replace('\\', '/');
                return normalizedInclude.EndsWith(
                    "/Applada.BuildingBlocks.Persistence/Applada.BuildingBlocks.Persistence.csproj",
                    StringComparison.OrdinalIgnoreCase);
            });

        Assert.True(persistenceReference);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repositorio Git nao encontrado.");
    }
}
