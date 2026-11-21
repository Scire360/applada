using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Communities.Api;
using Applada.Modules.Communities.Contracts;
using Applada.Modules.Communities.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Modules.Communities;

public static class CommunitiesModule
{
    public static IServiceCollection AddCommunitiesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<CommunitiesDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("applada")
                ?? throw new InvalidOperationException("ConnectionStrings:applada não configurada."),
            CommunitiesDbContext.ConfigureNpgsql));
        services.AddMigrationsOnStartup<CommunitiesDbContext>();
        services.AddOutboxPublisher<CommunitiesDbContext>();
        services.AddScoped<GroupStore>();
        services.AddScoped<IGroupMembership>(provider => provider.GetRequiredService<GroupStore>());

        return services;
    }

    public static IEndpointRouteBuilder MapCommunitiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGroupEndpoints();
    }
}
