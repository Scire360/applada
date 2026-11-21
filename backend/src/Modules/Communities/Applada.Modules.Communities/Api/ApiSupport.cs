using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Communities.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Applada.Modules.Communities.Api;

/// <summary>Página de listagem. <see cref="NextCursor"/> nulo: não há próxima página.</summary>
public sealed record GroupPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Cursor opaco: base64url de <c>kind|chave1|chave2|...</c>. Só posiciona a leitura; quem pode ver o quê continua
/// decidido pela consulta. Qualquer coisa fora do formato é recusada sem lançar.
/// </summary>
internal static class PageCursor
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    private const int MaxLength = 200;

    public static string Encode(string kind, params string[] keys) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join('|', [kind, .. keys])));

    public static string Key(DateTimeOffset value) => value.UtcTicks.ToString(CultureInfo.InvariantCulture);

    public static bool TryDecode(string? cursor, string kind, int keyCount, out string[] keys)
    {
        keys = [];
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var parts = text.Split('|');
        if (parts.Length != keyCount + 1 || parts[0] != kind)
        {
            return false;
        }

        keys = parts[1..];
        return true;
    }

    public static bool TryParseTime(string raw, out DateTimeOffset value)
    {
        value = default;
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    public static bool TryParseLimit(string? raw, out int limit)
    {
        if (string.IsNullOrEmpty(raw))
        {
            limit = DefaultLimit;
            return true;
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit is >= 1 and <= MaxLimit;
    }

    public static Error InvalidLimit { get; } = CommunityErrors.Validation("limit", $"O limite deve ser um inteiro entre 1 e {MaxLimit}.");

    public static Error InvalidCursor { get; } = CommunityErrors.Validation("cursor", "Cursor inválido.");
}

/// <summary>problem+json com <c>type = urn:applada:problem:&lt;code&gt;</c>, como no Matches.</summary>
internal static class Problems
{
    private const string TypePrefix = "urn:applada:problem:";

    public static IResult From(Error error)
    {
        ProblemDetails problem = error.Fields is { Count: > 0 } fields
            ? new HttpValidationProblemDetails(fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal))
            : new ProblemDetails();
        problem.Type = TypePrefix + error.Code;
        problem.Title = error.Message;
        problem.Status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
        return TypedResults.Problem(problem);
    }

    /// <summary>JSON com <c>Cache-Control: private, no-store</c>: o corpo depende de quem pergunta.</summary>
    public static IResult Private<T>(T value, int status = StatusCodes.Status200OK) => new PrivateJson<T>(value, status);

    private sealed class PrivateJson<T>(T value, int status) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.Json(value, statusCode: status).ExecuteAsync(httpContext);
        }
    }
}
