namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>A mesma API dos testes, mas no ambiente Development (superfície só de dev: OpenAPI e Scalar).</summary>
public sealed class DevelopmentApiFactory : ApiFactory
{
    public DevelopmentApiFactory()
        : base("Development")
    {
    }
}
