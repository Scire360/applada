using System.Globalization;
using Applada.Modules.Matches.Application.Queries;
using Microsoft.AspNetCore.Http;

namespace Applada.Modules.Matches.Api;

/// <summary>Apoio comum às rotas de listagem e inscrição: leitura do <c>limit</c> e respostas privadas.</summary>
internal static class ListingResults
{
    /// <summary>
    /// <c>limit</c> ausente ou vazio vale <see cref="PageLimit.Default"/>. Qualquer outra coisa precisa ser um inteiro
    /// sem sinal; a faixa (1..<see cref="PageLimit.Max"/>) é conferida pela consulta. Lido como texto, e não pelo binder,
    /// para que um valor não numérico vire o mesmo 400 <c>validation</c> com <c>errors.limit</c>.
    /// </summary>
    public static bool TryParseLimit(string? raw, out int limit)
    {
        if (string.IsNullOrEmpty(raw))
        {
            limit = PageLimit.Default;
            return true;
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit);
    }

    /// <summary>JSON com <c>Cache-Control: private, no-store</c>: o corpo depende de quem pergunta.</summary>
    public static IResult PrivateJson<T>(T value, int statusCode = StatusCodes.Status200OK) => new PrivateJsonResult<T>(value, statusCode);

    private sealed class PrivateJsonResult<T>(T value, int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.Json(value, statusCode: statusCode).ExecuteAsync(httpContext);
        }
    }
}
