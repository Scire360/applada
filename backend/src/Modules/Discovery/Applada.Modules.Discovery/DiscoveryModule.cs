using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Discovery.Api;
using Applada.Modules.Discovery.Contracts;
using Applada.Modules.Discovery.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Modules.Discovery;

public static class DiscoveryModule
{
    public const string DiscoverMatchesPath = "/api/v1/discover/matches";

    public static IServiceCollection AddDiscoveryModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<DiscoveryDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("applada")
                ?? throw new InvalidOperationException("ConnectionStrings:applada não configurada."),
            DiscoveryDbContext.ConfigureNpgsql));
        services.AddMigrationsOnStartup<DiscoveryDbContext>();
        services.AddScoped<VenueStore>();
        services.AddScoped<NearbyVenueQuery>();
        services.AddScoped<IVenueDirectory>(provider => provider.GetRequiredService<VenueStore>());
        return services;
    }

    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapVenueEndpoints().MapDiscoverEndpoints();
    }
}
