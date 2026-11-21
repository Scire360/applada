using Applada.Modules.Matches.Application.CancelMatch;
using Applada.Modules.Matches.Application.CreateMatch;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Application.Register;
using Applada.Modules.Matches.Application.UpdateMatch;
using Applada.Modules.Matches.Application.Withdraw;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Modules.Matches.Api;

public static class MatchesModule
{
    /// <summary>
    /// Registra o módulo inteiro (persistência, outbox, migrations e handlers). Chamar uma vez: a infraestrutura
    /// registra hosted services, que duplicariam se ela fosse registrada de novo.
    /// </summary>
    public static IServiceCollection AddMatchesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMatchesInfrastructure(configuration);
        services.AddScoped<CreateMatchHandler>();
        services.AddScoped<UpdateMatchHandler>();
        services.AddScoped<CancelMatchHandler>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<WithdrawHandler>();
        services.AddScoped<ParticipantsQuery>();
        services.AddScoped<MyMatchesQuery>();
        services.AddScoped<GroupMatchesQuery>();

        return services;
    }

    /// <summary>
    /// Mapeia <c>/api/v1/matches</c> (partidas e inscrições), <c>/api/v1/me/matches</c> e <c>/api/v1/groups/{id}/matches</c>. As escritas exigem a política de rate limiting <c>"writes"</c> registrada na
    /// raiz de composição.
    /// </summary>
    public static IEndpointRouteBuilder MapMatchesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapMatchEndpoints()
            .MapRegistrationEndpoints()
            .MapMyMatchesEndpoints()
            .MapGroupMatchesEndpoints();
    }
}
