using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Matches.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// <c>GET /api/v1/me/matches?role=organizer|participant&amp;cursor&amp;limit</c> lista as próximas partidas do usuário
/// autenticado por cursor. Para o organizador, inclui também as partidas com a chamada aberta.
/// </summary>
internal static class MyMatchesEndpoints
{
    public static IEndpointRouteBuilder MapMyMatchesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/matches", ListAsync).WithTags("Matches").WithName("ListMyMatches")
            // Metadados só para o documento OpenAPI: não alteram o comportamento da rota.
            .Produces<Page<MyMatchSummary>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? role,
        string? cursor,
        string? limit,
        ICurrentUser currentUser,
        MyMatchesQuery query,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!ListingResults.TryParseLimit(limit, out var pageSize))
        {
            return ProblemResults.From(PageLimit.Invalid);
        }

        var result = await query.HandleAsync(currentUser.UserId, role ?? string.Empty, cursor, pageSize, timeProvider.GetUtcNow(), ct);
        return result.IsSuccess ? ListingResults.PrivateJson(result.Value) : ProblemResults.From(result.Error);
    }
}
