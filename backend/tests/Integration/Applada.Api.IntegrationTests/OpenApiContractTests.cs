using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Applada.Api.IntegrationTests.Infrastructure;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// O contrato HTTP público fica versionado em <c>docs/api/openapi-v1.json</c>. Mudar uma rota, um schema ou
/// um código de resposta muda o documento gerado e este teste falha até o arquivo ser revisado e atualizado
/// (<c>UPDATE_OPENAPI=1</c>), de modo que toda quebra de contrato aparece no diff do commit.
/// </summary>
[Collection("api")]
public class OpenApiContractTests : IClassFixture<DevelopmentApiFactory>
{
    private const string UpdateVariable = "UPDATE_OPENAPI";
    private const string AuthorityPlaceholder = "{authority}";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly DevelopmentApiFactory _development;

    public OpenApiContractTests(DevelopmentApiFactory development) => _development = development;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenApi_document_matches_committed_file()
    {
        using var client = _development.CreateClient();
        var generated = Normalize(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct));

        var path = Path.Combine(FindRepositoryRoot(), "docs", "api", "openapi-v1.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Indent(generated) + "\n", new UTF8Encoding(false), Ct);
            return;
        }

        Assert.True(
            File.Exists(path),
            $"{path} não existe. Gere o arquivo com {UpdateVariable}=1 (cd backend; $env:{UpdateVariable}='1'; dotnet test --solution Applada.slnx --filter-class \"*OpenApiContractTests\").");

        var committed = Normalize(await File.ReadAllTextAsync(path, Ct));
        Assert.True(
            string.Equals(committed, generated, StringComparison.Ordinal),
            $"O documento OpenAPI gerado difere de docs/api/openapi-v1.json. Se a mudança de contrato é intencional, "
            + $"rode com {UpdateVariable}=1 (cd backend; $env:{UpdateVariable}='1'; dotnet test --solution Applada.slnx "
            + "--filter-class \"*OpenApiContractTests\"), revise o diff e inclua o arquivo no commit.");
    }

    /// <summary>
    /// Reserializa em forma compacta e remove o que depende do ambiente: <c>servers</c> (host e porta do servidor
    /// de teste) e o <c>Authentication:Authority</c> usado nas URLs do OAuth2 (issuer de teste aqui, realm do
    /// Keycloak no AppHost), trocado por <c>{authority}</c>.
    /// </summary>
    private static string Normalize(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("servers");
        var compact = root.ToJsonString();
        return compact.Replace(TestJwt.Issuer, AuthorityPlaceholder, StringComparison.Ordinal);
    }

    private static string Indent(string compactJson) =>
        JsonNode.Parse(compactJson)!.ToJsonString(Indented);

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "Applada.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("backend/Applada.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}
