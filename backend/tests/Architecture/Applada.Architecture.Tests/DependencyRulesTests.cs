using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Applada.Architecture.Tests;

/// <summary>
/// Regras de dependência entre camadas e módulos. Valem por padrão de namespace, não por nome de assembly:
/// o Identity é um assembly único com as camadas em namespaces (<c>Applada.Modules.Identity.Domain</c>, ...)
/// e os módulos futuros terão um assembly por camada com namespace igual ao nome do assembly.
/// O módulo é o segmento depois de <c>Applada.Modules.</c>. Os assemblies vêm das referências de projeto
/// deste teste: todo <c>Applada.*.dll</c> da pasta de saída entra, então um projeto novo só precisa ser referenciado.
/// </summary>
public class DependencyRulesTests
{
    private const string DomainLayer = @"^Applada\.(Modules\.[^.]+|BuildingBlocks)\.Domain(\.|$)";
    private const string ModuleDomain = @"^Applada\.Modules\.[^.]+\.Domain(\.|$)";
    private const string ModuleApplication = @"^Applada\.Modules\.[^.]+\.Application(\.|$)";
    private const string ModuleInfrastructure = @"^Applada\.Modules\.[^.]+\.Infrastructure(\.|$)";
    private const string ModuleApi = @"^Applada\.Modules\.[^.]+\.Api(\.|$)";
    private const string ModuleContracts = @"^Applada\.Modules\.[^.]+\.Contracts(\.|$)";
    private const string EfCore = @"^Microsoft\.EntityFrameworkCore(\.|$)";
    private const string AspNetCore = @"^Microsoft\.AspNetCore(\.|$)";
    private const string Npgsql = @"^Npgsql(\.|$)";
    private const string RabbitMq = @"^RabbitMQ(\.|$)";

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> LazyArchitecture = new(LoadArchitecture);

    private static ArchUnitNET.Domain.Architecture Arch => LazyArchitecture.Value;

    [Fact]
    public void Domain_does_not_depend_on_infrastructure_api_efcore_or_aspnet()
    {
        var domain = Types().That().ResideInNamespaceMatching(DomainLayer).As("Domain");
        AssertNotEmpty(domain, "Domain");

        var rule = domain.Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching(ModuleApplication)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(ModuleInfrastructure)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(ModuleApi)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(EfCore)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(AspNetCore);

        AssertRule(rule);
    }

    [Fact]
    public void Application_depends_only_on_domain_and_contracts()
    {
        // Ainda não existe módulo com camada Application (chega com o Matches): vazio é esperado por ora.
        var application = Types().That().ResideInNamespaceMatching(ModuleApplication).As("Application");

        var rule = application.Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching(ModuleInfrastructure)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(ModuleApi)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(AspNetCore)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(Npgsql)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceMatching(RabbitMq);

        AssertRule(rule.WithoutRequiringPositiveResults());
    }

    [Fact]
    public void Modules_reference_other_modules_only_through_contracts()
    {
        var modules = ModuleNames();
        Assert.NotEmpty(modules);

        // Applada.Api (raiz de composição) e os testes ficam fora de Applada.Modules.*, logo isentos.
        var violations = new List<string>();
        foreach (var module in modules)
        {
            var name = Regex.Escape(module);
            var otherModuleInternals = $@"^Applada\.Modules\.(?!{name}(\.|$))[^.]+(?:\.(?!Contracts(?:\.|$))|$)";

            var rule = Types().That().ResideInNamespaceMatching($@"^Applada\.Modules\.{name}(\.|$)").As($"Módulo {module}")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(otherModuleInternals);

            violations.AddRange(Violations(rule.WithoutRequiringPositiveResults()));
        }

        Assert.True(violations.Count == 0, FormatViolations(violations));
    }

    [Fact]
    public void Contracts_do_not_depend_on_module_internals()
    {
        var contracts = Types().That().ResideInNamespaceMatching(ModuleContracts).As("Contracts");
        AssertNotEmpty(contracts, "Contracts");

        var violations = new List<string>();
        foreach (var module in ModuleNames())
        {
            var name = Regex.Escape(module);
            var ownInternals = $@"^Applada\.Modules\.{name}(?:\.(?!Contracts(?:\.|$))|$)";

            var rule = Types().That().ResideInNamespaceMatching($@"^Applada\.Modules\.{name}\.Contracts(\.|$)").As($"Contracts de {module}")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(ownInternals);

            violations.AddRange(Violations(rule.WithoutRequiringPositiveResults()));
        }

        Assert.True(violations.Count == 0, FormatViolations(violations));
    }

    [Fact]
    public void No_generic_repository()
    {
        // GenericRepository<...> ou IRepository<...> (nome de metadados: IRepository`1).
        var rule = Types().That().HaveNameMatching(@"^(GenericRepository|IRepository)(`\d+)?$|^GenericRepository")
            .Should().NotExist();

        AssertRule(rule);
    }

    [Fact]
    public void Every_src_project_is_covered_by_the_architecture_rules()
    {
        var srcRoot = Path.Combine(FindBackendRoot(), "src");
        var expected = Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => name != "Applada.AppHost")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(expected);

        var loaded = LoadedAssemblies.Value.Select(a => a.GetName().Name).ToHashSet(StringComparer.Ordinal);

        // Referência direta, não só transitiva: um projeto que some da lista escaparia sem ninguém notar.
        var csproj = Path.Combine(FindBackendRoot(), "tests", "Architecture", "Applada.Architecture.Tests", "Applada.Architecture.Tests.csproj");
        var referenced = System.Xml.Linq.XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(e => ProjectNameFromInclude((string?)e.Attribute("Include") ?? string.Empty))
            .ToHashSet(StringComparer.Ordinal);

        var missing = expected.Where(name => !loaded.Contains(name) || !referenced.Contains(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            "Projetos de src fora das regras de arquitetura (faltou ProjectReference em Applada.Architecture.Tests.csproj): " + string.Join(", ", missing));
    }

    private static readonly Lazy<System.Reflection.Assembly[]> LoadedAssemblies = new(() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Applada.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.Ordinal))
            .Select(System.Reflection.Assembly.LoadFrom)
            .ToArray());

    /// <summary>Nome do projeto de um <c>Include</c> do csproj, que usa <c>\</c> mesmo quando o teste roda em Linux.</summary>
    internal static string ProjectNameFromInclude(string include) =>
        Path.GetFileNameWithoutExtension(include[(include.LastIndexOfAny(['\\', '/']) + 1)..]);

    [Theory]
    [InlineData(@"..\..\..\src\Applada.Api\Applada.Api.csproj", "Applada.Api")]
    [InlineData("../../../src/Applada.Api/Applada.Api.csproj", "Applada.Api")]
    [InlineData("Applada.Api.csproj", "Applada.Api")]
    public void ProjectNameFromInclude_handles_both_path_separators(string include, string expected) =>
        Assert.Equal(expected, ProjectNameFromInclude(include));

    private static string FindBackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Applada.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Applada.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }

    private static ArchUnitNET.Domain.Architecture LoadArchitecture()
    {
        var assemblies = LoadedAssemblies.Value;

        // Se isto zerar, a regra passaria sobre nada: falha alto.
        Assert.NotEmpty(assemblies);

        return new ArchLoader().LoadAssemblies(assemblies).Build();
    }

    private static string[] ModuleNames() =>
        Arch.Types
            .Select(t => Regex.Match(t.Namespace?.FullName ?? string.Empty, @"^Applada\.Modules\.([^.]+)(\.|$)"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertNotEmpty(ArchUnitNET.Fluent.Syntax.Elements.Types.GivenTypesConjunctionWithDescription selection, string layer)
    {
        var count = selection.GetObjects(Arch).Count();
        Assert.True(count > 0, $"A seleção '{layer}' está vazia: a regra não estaria verificando nada.");
    }

    private static void AssertRule(IArchRule rule) =>
        Assert.True(Violations(rule).Count == 0, FormatViolations(Violations(rule)));

    private static List<string> Violations(IArchRule rule) =>
        rule.Evaluate(Arch).Where(r => !r.Passed).Select(r => r.Description).ToList();

    private static string FormatViolations(List<string> violations) =>
        violations.Count == 0 ? string.Empty : "Violações de arquitetura:" + Environment.NewLine + string.Join(Environment.NewLine, violations);
}
