using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Applada.Api.Errors;

public static class ProblemDetailsSetup
{
    private const string TypePrefix = "urn:applada:problem:";

    /// <summary>
    /// Registra problem+json para todo erro. Nunca inclui detalhe de exceção, em nenhum ambiente.
    /// </summary>
    public static IServiceCollection AddApladaProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            if (IsFrameworkDefaultType(problem.Type))
            {
                problem.Type = TypePrefix + CodeFor(status);
            }

            if (status >= StatusCodes.Status500InternalServerError || context.Exception is not null)
            {
                problem.Detail = null;
            }

            problem.Extensions.Remove("exception");
            problem.Extensions.Remove("stackTrace");
            problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });

        return services;
    }

    public static WebApplication UseApladaProblemDetails(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }

    // Só substitui o type padrão do framework (link RFC) ou ausente; type específico de domínio é preservado.
    private static bool IsFrameworkDefaultType(string? type) =>
        string.IsNullOrEmpty(type)
        || type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal)
        || type.StartsWith("https://datatracker.ietf.org/", StringComparison.Ordinal);

    // "internal" só para 5xx; demais 4xx sem código dedicado caem em "request-error".
    private static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "validation",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
        StatusCodes.Status415UnsupportedMediaType => "unsupported-media-type",
        >= StatusCodes.Status500InternalServerError => "internal",
        _ => "request-error",
    };
}
