using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Application.RecordAttendance;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Matches.Api;

internal static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/matches/{id:guid}/attendance").WithTags("Matches");
        group.MapPut("/{userId:guid}", RecordAsync).WithName("RecordAttendance").RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces<AttendanceResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet(string.Empty, ListAsync).WithName("ListAttendance")
            .Produces<Page<AttendanceEntryResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static async Task<IResult> RecordAsync(Guid id, Guid userId, RecordAttendanceRequest request, ICurrentUser currentUser,
        RecordAttendanceHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(id, currentUser.UserId, userId, request.Status, ct);
        if (!result.IsSuccess)
        {
            return ProblemResults.From(result.Error);
        }

        var recorded = result.Value;
        return ListingResults.PrivateJson(new AttendanceResponse(recorded.UserId, recorded.Status.ToContractText(), recorded.RecordedAt));
    }

    private static async Task<IResult> ListAsync(Guid id, string? cursor, string? limit, ICurrentUser currentUser,
        AttendanceQuery query, CancellationToken ct)
    {
        if (!ListingResults.TryParseLimit(limit, out var pageSize))
        {
            return ProblemResults.From(PageLimit.Invalid);
        }

        var result = await query.HandleAsync(id, currentUser.UserId, cursor, pageSize, ct);
        if (!result.IsSuccess)
        {
            return ProblemResults.From(result.Error);
        }

        var page = result.Value;
        return ListingResults.PrivateJson(new Page<AttendanceEntryResponse>(
            [.. page.Items.Select(e => new AttendanceEntryResponse(e.UserId, e.DisplayName, e.Status, e.RecordedAt))], page.NextCursor));
    }
}
