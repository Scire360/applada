using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Application.Register;
using Applada.Modules.Matches.Application.Withdraw;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// Inscrições em <c>/api/v1/matches/{id}/registrations</c>. Nenhuma rota recebe <c>userId</c> de terceiro: inscrever e
/// sair agem sempre sobre o usuário autenticado (IDOR eliminado por desenho). A lista de participantes é autorizada
/// pela consulta (organizador ou inscrito ativo). Escritas usam a política <see cref="RateLimitPolicies.Writes"/>.
/// </summary>
internal static partial class RegistrationEndpoints
{
    private const string LoggerCategory = "Applada.Modules.Matches.Api.RegistrationEndpoints";

    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/matches/{id:guid}/registrations").WithTags("Matches");

        group.MapPost(string.Empty, RegisterAsync).WithName("Register").RequireRateLimiting(RateLimitPolicies.Writes)
            // Metadados só para o documento OpenAPI: não alteram o comportamento da rota.
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .Produces<RegistrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/me", WithdrawAsync).WithName("Withdraw").RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet(string.Empty, ListAsync).WithName("ListParticipants")
            .Produces<Page<ParticipantResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>201 quando cria ou reativa a inscrição; 200 com o estado atual quando ela já estava ativa.</summary>
    private static async Task<IResult> RegisterAsync(Guid id, ICurrentUser currentUser, RegisterHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(id, currentUser.UserId, ct);
        if (!result.IsSuccess)
        {
            return ProblemResults.From(result.Error);
        }

        var registration = result.Value;
        return ListingResults.PrivateJson(
            new RegistrationResponse(registration.Status, registration.Position),
            registration.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> WithdrawAsync(
        Guid id,
        ICurrentUser currentUser,
        WithdrawHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        Result result;
        try
        {
            result = await handler.HandleAsync(id, currentUser.UserId, ct);
        }
        catch (MatchStateDivergenceException ex)
        {
            // Só a divergência contador × inscrições do domínio: a transação já foi desfeita e vira 500. Qualquer outra
            // exceção sobe intocada até o tratador global.
            LogStateDivergence(loggerFactory.CreateLogger(LoggerCategory), id, ex);
            throw;
        }

        return result.IsSuccess ? TypedResults.NoContent() : ProblemResults.From(result.Error);
    }

    private static async Task<IResult> ListAsync(
        Guid id,
        string? cursor,
        string? limit,
        ICurrentUser currentUser,
        ParticipantsQuery query,
        CancellationToken ct)
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
        return ListingResults.PrivateJson(new Page<ParticipantResponse>([.. page.Items.Select(ParticipantResponse.From)], page.NextCursor));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Contadores da partida {MatchId} divergem das inscrições; saída abortada.")]
    private static partial void LogStateDivergence(ILogger logger, Guid matchId, Exception exception);
}
