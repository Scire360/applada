using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Profiles.Api;
using Applada.Modules.Profiles.Contracts;
using Applada.Modules.Profiles.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Modules.Profiles;

public static class ProfilesModule
{
    public static IServiceCollection AddProfilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<ProfilesDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("applada")
                ?? throw new InvalidOperationException("ConnectionStrings:applada não configurada."),
            ProfilesDbContext.ConfigureNpgsql));
        services.AddMigrationsOnStartup<ProfilesDbContext>();
        services.AddScoped<ProfileStore>();
        services.AddScoped<IProfileNames>(provider => provider.GetRequiredService<ProfileStore>());

        return services;
    }

    public static IEndpointRouteBuilder MapProfilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapProfileEndpoints();
    }
}
