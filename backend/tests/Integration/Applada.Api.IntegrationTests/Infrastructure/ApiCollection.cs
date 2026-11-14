namespace Applada.Api.IntegrationTests.Infrastructure;

// O nome "ApiCollection" segue o padrão xUnit de collection definition (CollectionDefinition("api")).
#pragma warning disable CA1711
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
#pragma warning restore CA1711
