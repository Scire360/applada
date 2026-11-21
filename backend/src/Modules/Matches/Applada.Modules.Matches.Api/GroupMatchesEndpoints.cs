using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Matches.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// <c>GET /api/v1/groups/{id}/matches?cursor&amp;limit</c>: próximas partidas agendadas do grupo, só para membros, por
/// cursor. "Próximas" é <c>starts_at &gt;= agora</c> pelo <see cref="TimeProvider"/>.
/// </summary>
internal static class GroupMatchesEndpoints
{
    public static IEndpointRouteBuilder MapGroupMatchesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/groups/{id:guid}/matches", ListAsync).WithTags("Matches").WithName("ListGroupMatches")
            // Metadados só para o documento OpenAPI: não alteram o comportamento da rota.
            .Produces<Page<MyMatchSummary>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        string? cursor,
        string? limit,
        ICurrentUser currentUser,
        GroupMatchesQuery query,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!ListingResults.TryParseLimit(limit, out var pageSize))
        {
            return ProblemResults.From(PageLimit.Invalid);
        }

        var result = await query.HandleAsync(id, currentUser.UserId, cursor, pageSize, timeProvider.GetUtcNow(), ct);
        return result.IsSuccess ? ListingResults.PrivateJson(result.Value) : ProblemResults.From(result.Error);
    }
}
