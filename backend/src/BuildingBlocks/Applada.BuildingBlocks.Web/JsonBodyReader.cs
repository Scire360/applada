using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace Applada.BuildingBlocks.Web;

public static class JsonBodyReader
{
    public static async Task<(T? Value, IResult? Failure)> ReadJsonBodyAsync<T>(
        HttpRequest request, int maxBytes, JsonSerializerOptions options, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfEqual(maxBytes, int.MaxValue);

        if (!request.HasJsonContentType() || GetEncoding(request) is not { } encoding)
        {
            return (null, TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "O corpo deve ser JSON com uma codificação reconhecida.", type: "urn:applada:problem:unsupported-media-type"));
        }

        if (request.ContentLength > maxBytes)
        {
            return (null, PayloadTooLarge());
        }

        // O teto vale nos bytes recebidos, antes da transcodificação e mesmo sem Content-Length.
        var buffer = new byte[maxBytes + 1];
        var length = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        if (length > maxBytes)
        {
            return (null, PayloadTooLarge());
        }

        // O desserializador pode ignorar bytes malformados em uma propriedade desconhecida.
        var isUtf8 = encoding.CodePage == Encoding.UTF8.CodePage;
        if (isUtf8 && !Utf8.IsValid(buffer.AsSpan(0, length)))
        {
            return (null, Invalid("body"));
        }

        try
        {
            using var stream = new MemoryStream(buffer, 0, length, writable: false);
            using var utf8 = isUtf8 ? null : Encoding.CreateTranscodingStream(stream, encoding, Encoding.UTF8);
            var value = await JsonSerializer.DeserializeAsync<T>(utf8 ?? stream, options, ct);
            return value is null ? (null, Invalid("body")) : (value, null);
        }
        catch (JsonException ex)
        {
            return (null, Invalid(FieldFromPath<T>(ex.Path, options)));
        }
        catch (DecoderFallbackException)
        {
            return (null, Invalid("body"));
        }
    }

    private static Encoding? GetEncoding(HttpRequest request)
    {
        var charset = request.GetTypedHeaders().ContentType!.Charset;
        if (!charset.HasValue)
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(HeaderUtilities.RemoveQuotes(charset).Value!,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static ProblemHttpResult PayloadTooLarge() => TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Corpo da requisição grande demais.", type: "urn:applada:problem:payload-too-large");

    private static ValidationProblem Invalid(string field) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = ["Valor ausente, malformado ou de tipo inválido."],
        }, title: "Dados inválidos.", type: "urn:applada:problem:validation");

    // Só devolve o nome publicado de uma propriedade do DTO, nunca texto arbitrário do cliente.
    private static string FieldFromPath<T>(string? path, JsonSerializerOptions options)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return "body";
        }

        var segment = path.AsSpan(2);
        var end = segment.IndexOfAny('.', '[');
        if (end >= 0)
        {
            segment = segment[..end];
        }

        foreach (var property in options.GetTypeInfo(typeof(T)).Properties)
        {
            if (segment.Equals(property.Name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Name;
            }
        }

        return "body";
    }
}
