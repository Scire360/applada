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

    private static ArchUnitNET.Domain.Architecture LoadArchitecture()
    {
        var assemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "Applada.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.Ordinal))
            .Select(System.Reflection.Assembly.LoadFrom)
            .ToArray();

        // Se isto zerar, a regra passaria sobre nada: falha alto.
        Assert.True(assemblies.Length >= 6, $"Assemblies Applada.* carregados: {assemblies.Length} (esperado >= 6).");

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
