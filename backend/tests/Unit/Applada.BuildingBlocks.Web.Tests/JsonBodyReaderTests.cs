using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Applada.BuildingBlocks.Web.Tests;

public class JsonBodyReaderTests
{
    private const int Limit = 64;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, Limit)]
    [InlineData(false, Limit)]
    [InlineData(true, Limit + 1)]
    [InlineData(false, Limit + 1)]
    [InlineData(false, Limit * 4)]
    public async Task Enforces_byte_limit_with_and_without_content_length(bool declaredLength, int length)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":"Arena"}""".PadRight(length)));
        var request = Request(stream, "application/json");
        request.ContentLength = declaredLength ? length : null;

        var (value, failure) = await JsonBodyReader.ReadJsonBodyAsync<Body>(request, Limit, JsonSerializerOptions.Web, Ct);

        if (length == Limit)
        {
            Assert.Null(failure);
            Assert.Equal("Arena", value!.Name);
        }
        else
        {
            Assert.Null(value);
            var problem = Assert.IsType<ProblemHttpResult>(failure);
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, problem.StatusCode);
            Assert.Equal("urn:applada:problem:payload-too-large", problem.ProblemDetails.Type);
            Assert.Equal(declaredLength ? 0 : Limit + 1, stream.Position);
        }
    }

    [Theory]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; charset=\"utf-16\"")]
    public async Task Transcodes_utf16_with_limit_in_original_bytes(string contentType)
    {
        var bytes = Encoding.Unicode.GetBytes("""{"name":"Praça"}""");
        using var stream = new MemoryStream(bytes);
        var request = Request(stream, contentType);
        var (value, failure) = await JsonBodyReader.ReadJsonBodyAsync<Body>(request, bytes.Length, JsonSerializerOptions.Web, Ct);
        Assert.Null(failure);
        Assert.Equal("Praça", value!.Name);

        stream.Position = 0;
        var (tooLarge, rejected) = await JsonBodyReader.ReadJsonBodyAsync<Body>(request, bytes.Length - 1, JsonSerializerOptions.Web, Ct);
        Assert.Null(tooLarge);
        var problem = Assert.IsType<ProblemHttpResult>(rejected);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problem.StatusCode);
        Assert.Equal("urn:applada:problem:payload-too-large", problem.ProblemDetails.Type);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=unknown-charset")]
    public async Task Unsupported_content_type_or_charset_returns_415(string contentType)
    {
        using var stream = new MemoryStream("{}"u8.ToArray());
        var (_, failure) = await JsonBodyReader.ReadJsonBodyAsync<Body>(Request(stream, contentType), Limit, JsonSerializerOptions.Web, Ct);
        var problem = Assert.IsType<ProblemHttpResult>(failure);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, problem.StatusCode);
        Assert.Equal("urn:applada:problem:unsupported-media-type", problem.ProblemDetails.Type);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Unpaired_surrogate_in_utf16_returns_validation_error_for_body()
    {
        // D800 em little-endian, sem o segundo surrogate do par.
        byte[] bytes = [.. Encoding.Unicode.GetBytes("{\"name\":\""), 0x00, 0xD8, .. Encoding.Unicode.GetBytes("\"}")];
        using var stream = new MemoryStream(bytes);
        var request = Request(stream, "application/json; charset=utf-16");

        var (value, failure) = await JsonBodyReader.ReadJsonBodyAsync<Body>(request, Limit, JsonSerializerOptions.Web, Ct);

        var problem = Assert.IsType<ValidationProblem>(failure);
        Assert.Null(value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("urn:applada:problem:validation", problem.ProblemDetails.Type);
        Assert.Equal("body", Assert.Single(problem.ProblemDetails.Errors).Key);
    }

    [Theory]
    [InlineData("""{"NAME":!}""", "name")]
    [InlineData("""{"unknown":!}""", "body")]
    [InlineData("{", "body")]
    [InlineData("null", "body")]
    public async Task Invalid_json_names_only_known_fields(string json, string field)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var (value, failure) = await JsonBodyReader.ReadJsonBodyAsync<Body>(Request(stream, "application/json"), Limit, JsonSerializerOptions.Web, Ct);
        Assert.Null(value);
        var problem = Assert.IsType<ValidationProblem>(failure);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("urn:applada:problem:validation", problem.ProblemDetails.Type);
        Assert.Equal(field, Assert.Single(problem.ProblemDetails.Errors).Key);
    }

    private static HttpRequest Request(Stream body, string contentType)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        request.ContentType = contentType;
        return request;
    }

    private sealed record Body(string? Name);
}
