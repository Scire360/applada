using System.Net;
using System.Text.Json;

namespace Applada.Api.IntegrationTests.Infrastructure;

public static class ProblemAssertions
{
    public static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(status == response.StatusCode, $"Esperado {(int)status}, veio {(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(raw);
        Assert.Equal($"urn:applada:problem:{code}", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        return problem;
    }
}
