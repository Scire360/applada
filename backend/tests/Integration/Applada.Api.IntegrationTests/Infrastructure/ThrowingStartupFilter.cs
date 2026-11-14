using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Acrescenta, depois do pipeline da API, um middleware com paths só de teste:
/// /test/throw (lança), /test/problem (problem de domínio) e /test/auth (resultado da autenticação).
/// Só é alcançado por paths sem endpoint, então a API não ganha rota de teste.
/// </summary>
public sealed class ThrowingStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                switch (context.Request.Path.Value)
                {
                    case "/test/throw":
                        throw new InvalidOperationException("segredo-interno");
                    case "/test/problem":
                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = context,
                            ProblemDetails = new ProblemDetails
                            {
                                Type = "urn:applada:problem:registration-closed",
                                Title = "Inscrições encerradas",
                                Status = StatusCodes.Status409Conflict,
                                Detail = "detalhe-de-dominio",
                            },
                        });
                        break;
                    case "/test/auth":
                        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                        context.Response.StatusCode = result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized;
                        break;
                    default:
                        await nextMiddleware();
                        break;
                }
            });
        };
}
