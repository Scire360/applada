using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Acrescenta, depois do pipeline da API, um middleware que lança para /test/throw.
/// Só é alcançado por paths sem endpoint, então a API não ganha rota de teste.
/// </summary>
public sealed class ThrowingStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
                context.Request.Path == "/test/throw"
                    ? throw new InvalidOperationException("segredo-interno")
                    : nextMiddleware());
        };
}
