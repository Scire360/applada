using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Profiles.Domain;
using Applada.Modules.Profiles.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Applada.Modules.Profiles.Api;

public sealed record ProfileResponse(string? DisplayName);

public sealed record DisplayNameRequest(string DisplayName);

internal static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var profile = endpoints.MapGroup("/api/v1/me/profile").WithTags("Profiles").RequireAuthorization();

        profile.MapGet(string.Empty, ReadAsync)
            .WithName("GetMyProfile")
            .WithSummary("Lê o nome de exibição do usuário autenticado, que pode estar vazio.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        profile.MapPut(string.Empty, SaveAsync)
            .WithName("SetMyDisplayName")
            .WithSummary("Salva o nome de exibição do usuário autenticado, de 1 a 80 caracteres após remover espaços nas pontas.")
            .Accepts<DisplayNameRequest>("application/json")
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<Ok<ProfileResponse>> ReadAsync(
        ICurrentUser currentUser, ProfileStore store, HttpResponse response, CancellationToken ct)
    {
        var displayName = await store.FindAsync(currentUser.UserId, ct);
        response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(new ProfileResponse(displayName));
    }

    private static async Task<Results<Ok<ProfileResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        HttpRequest request, ICurrentUser currentUser, ProfileStore store, HttpResponse response, CancellationToken ct)
    {
        if (!request.HasJsonContentType() || request.GetTypedHeaders().ContentType is { Charset.HasValue: true, Encoding: null })
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "O corpo deve ser JSON com uma codificação reconhecida.",
                type: "urn:applada:problem:unsupported-media-type");
        }

        DisplayNameRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<DisplayNameRequest>(ct);
        }
        catch (JsonException)
        {
            return InvalidDisplayName();
        }

        var displayName = DisplayNameRules.Normalize(body?.DisplayName);
        if (displayName is null)
        {
            return InvalidDisplayName();
        }

        await store.SaveAsync(currentUser.UserId, displayName, ct);
        response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(new ProfileResponse(displayName));
    }

    private static ValidationProblem InvalidDisplayName() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["displayName"] = [$"Informe um nome de exibição de 1 a {DisplayNameRules.MaxLength} caracteres, sem caracteres de controle."],
        }, type: "urn:applada:problem:validation");
}
