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
            problem.Type = TypeFor(problem.Status ?? context.HttpContext.Response.StatusCode);
            problem.Detail = null;
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

    private static string TypeFor(int status) => TypePrefix + status switch
    {
        StatusCodes.Status400BadRequest => "validation",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        _ => "internal",
    };
}
