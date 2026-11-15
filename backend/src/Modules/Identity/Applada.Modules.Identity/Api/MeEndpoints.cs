using Applada.BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Identity.Api;

public sealed record MeResponse(Guid UserId);

internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me", GetMe)
            .WithName("GetMe")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static Ok<MeResponse> GetMe(ICurrentUser currentUser) => TypedResults.Ok(new MeResponse(currentUser.UserId));
}
