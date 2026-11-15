using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// Converte um <see cref="Error"/> em problem+json com <c>type = urn:applada:problem:&lt;code&gt;</c> e o status da
/// tabela de códigos de erro. <c>validation</c> leva a extensão <c>errors</c>; <c>lock-timeout</c> leva
/// <c>Retry-After: 1</c>. O corpo passa pelo <see cref="IProblemDetailsService"/> da API (traceId, sem detalhe interno).
/// </summary>
/// <remarks>
/// Fica no módulo, não em <c>Applada.Api</c>: os endpoints de Matches precisam dele e o módulo não pode referenciar
/// a raiz de composição (dependência circular).
/// </remarks>
internal static class ProblemResults
{
    private const string TypePrefix = "urn:applada:problem:";

    public static IResult From(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        ProblemDetails problem = error.Fields is { Count: > 0 } fields
            ? new HttpValidationProblemDetails(fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal))
            : new ProblemDetails();
        problem.Type = TypePrefix + error.Code;
        problem.Title = error.Message;
        problem.Status = StatusFor(error);

        var result = TypedResults.Problem(problem);
        return error.Code == MatchErrors.LockTimeout.Code ? new RetryAfterResult(result) : result;
    }

    internal static int StatusFor(Error error)
    {
        // Mesma categoria (pré-condição), status diferente: sem If-Match é 428, If-Match divergente é 412.
        if (error.Code == MatchErrors.IfMatchRequired.Code)
        {
            return StatusCodes.Status428PreconditionRequired;
        }

        return error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Categoria de erro sem status HTTP."),
        };
    }

    private sealed class RetryAfterResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.RetryAfter = "1";
            return inner.ExecuteAsync(httpContext);
        }
    }
}
