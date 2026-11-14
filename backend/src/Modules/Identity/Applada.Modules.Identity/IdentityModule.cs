using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Identity.Api;
using Applada.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Modules.Identity;

public static class IdentityModule
{
    private const string ConnectionStringName = "applada";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Connection string lida na criação do contexto, não no registro.
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} não configurada."),
            IdentityDbContext.ConfigureNpgsql));
        services.AddMigrationsOnStartup<IdentityDbContext>();

        services.AddScoped<UserProvisioner>();
        services.AddScoped<HttpCurrentUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<HttpCurrentUser>());

        return services;
    }

    /// <summary>
    /// Resolve o usuário interno da requisição. Registrar depois de <c>UseAuthentication</c> e antes de <c>UseAuthorization</c>.
    /// </summary>
    public static IApplicationBuilder UseCurrentUser(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CurrentUserMiddleware>();
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapMeEndpoints();
    }
}
