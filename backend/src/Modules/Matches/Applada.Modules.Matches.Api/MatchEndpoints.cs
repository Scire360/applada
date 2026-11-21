using System.Globalization;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Domain;
using Applada.BuildingBlocks.Web;
using Applada.Modules.Matches.Application.CancelMatch;
using Applada.Modules.Matches.Application.CreateMatch;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Application.UpdateMatch;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Applada.Modules.Matches.Api;

/// <summary>
/// Rotas de partida em <c>/api/v1/matches</c>. Todas exigem usuário autenticado (política fallback) com id interno
/// resolvido; a autorização por recurso (só o organizador edita e cancela) é do domínio. Escritas usam a política de
/// rate limiting <see cref="RateLimitPolicies.Writes"/>, definida pela raiz de composição.
/// </summary>
internal static partial class MatchEndpoints
{
    private const string BasePath = "/api/v1/matches";
    private const int IdempotencyKeyMaxLength = 100;

    /// <summary>Limite do corpo das escritas: o maior corpo válido (título e local no limite, todo escapado) fica bem abaixo.</summary>
    private const int MaxBodyBytes = 16 * 1024;
    private const string LoggerCategory = "Applada.Modules.Matches.Api.MatchEndpoints";

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).WithTags("Matches");

        group.MapPost(string.Empty, CreateAsync).WithName("CreateMatch").RequireRateLimiting(RateLimitPolicies.Writes)
            // Metadados só para o documento OpenAPI: não alteram o comportamento da rota.
            .Produces<MatchResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetMatch")
            .Produces<MatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateMatch").RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces<MatchResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/{id:guid}/cancellation", CancelAsync).WithName("CancelMatch").RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces<MatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        ICurrentUser currentUser,
        CreateMatchHandler handler,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken ct)
    {
        if (!TryReadIdempotencyKey(request.Headers, out var idempotencyKey))
        {
            return ProblemResults.From(MatchErrors.IdempotencyKeyMissing);
        }

        var json = jsonOptions.Value.SerializerOptions;
        var body = await JsonBodyReader.ReadJsonBodyAsync<CreateMatchRequest>(request, MaxBodyBytes, json, ct);
        if (body.Failure is { } failure)
        {
            return failure;
        }

        // Hash do conteúdo, não dos bytes: reserializado com as opções da API, espaços e ordem de campos não contam.
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body.Value!, json)));

        var result = await handler.HandleAsync(
            body.Value!.ToNewMatch(),
            currentUser.UserId,
            idempotencyKey,
            requestHash,
            match => (StatusCodes.Status201Created, JsonSerializer.Serialize(MatchResponse.From(MatchDetails.From(match, null)), json)),
            ct);

        return result.IsSuccess
            ? new StoredResponseResult(result.Value.Status, result.Value.Body)
            : ProblemResults.From(result.Error);
    }

    private static async Task<IResult> GetAsync(Guid id, ICurrentUser currentUser, IMatchQueries queries, CancellationToken ct)
    {
        // Nesta fatia toda partida é pública: qualquer usuário autenticado vê os dados e os contadores.
        var details = await queries.GetAsync(id, currentUser.UserId, ct);
        return details is null ? ProblemResults.From(MatchErrors.MatchNotFound) : new MatchResult(details);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        HttpRequest request,
        ICurrentUser currentUser,
        UpdateMatchHandler handler,
        IOptions<JsonOptions> jsonOptions,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var ifMatch = request.Headers.IfMatch;
        if (ifMatch.Count == 0)
        {
            return ProblemResults.From(MatchErrors.IfMatchRequired);
        }

        if (!TryParseVersion(ifMatch, out var expectedVersion))
        {
            return ProblemResults.From(MatchErrors.VersionMismatch);
        }

        var body = await JsonBodyReader.ReadJsonBodyAsync<UpdateMatchRequest>(request, MaxBodyBytes, jsonOptions.Value.SerializerOptions, ct);
        if (body.Failure is { } failure)
        {
            return failure;
        }

        Result<MatchDetails> result;
        try
        {
            var changes = body.Value!;
            result = await handler.HandleAsync(id, currentUser.UserId, expectedVersion, changes.Title, changes.LocationText, changes.Capacity, ct);
        }
        catch (MatchStateDivergenceException ex)
        {
            // Só a divergência contador × inscrições do domínio: integridade primeiro, a transação já foi desfeita e vira
            // 500. Qualquer outra exceção sobe intocada (500 comum, logado pelo tratador global).
            LogStateDivergence(loggerFactory.CreateLogger(LoggerCategory), id, ex);
            throw;
        }

        return result.IsSuccess ? new MatchResult(result.Value) : ProblemResults.From(result.Error);
    }

    private static async Task<IResult> CancelAsync(Guid id, ICurrentUser currentUser, CancelMatchHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(id, currentUser.UserId, ct);
        return result.IsSuccess ? new MatchResult(result.Value) : ProblemResults.From(result.Error);
    }

    /// <summary>Exatamente um valor, de 1 a 100 caracteres ASCII imprimíveis (0x20–0x7E).</summary>
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string key)
    {
        key = string.Empty;
        var values = headers["Idempotency-Key"];
        if (values.Count != 1 || values[0] is not { Length: > 0 and <= IdempotencyKeyMaxLength } value)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        key = value;
        return true;
    }

    /// <summary>Aceita <c>W/"n"</c> ou <c>"n"</c>, um só valor; <c>*</c>, listas e qualquer outra forma são recusados.</summary>
    private static bool TryParseVersion(StringValues ifMatch, out int version)
    {
        version = 0;
        if (ifMatch.Count != 1 || ifMatch[0] is not { } raw)
        {
            return false;
        }

        var tag = raw.Trim().AsSpan();
        if (tag.StartsWith("W/", StringComparison.Ordinal))
        {
            tag = tag[2..];
        }

        return tag.Length >= 3
            && tag[0] == '"'
            && tag[^1] == '"'
            && int.TryParse(tag[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Contadores da partida {MatchId} divergem das inscrições; edição abortada.")]
    private static partial void LogStateDivergence(ILogger logger, Guid matchId, Exception exception);

    /// <summary>
    /// <see cref="MatchResponse"/> com <c>ETag: W/"version"</c> (versão dos dados editáveis, não do corpo) e
    /// <c>Cache-Control: private, no-store</c>: o corpo traz a inscrição de quem consulta.
    /// </summary>
    private sealed class MatchResult(MatchDetails details) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.ETag = $"W/\"{details.Version.ToString(CultureInfo.InvariantCulture)}\"";
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.Ok(MatchResponse.From(details)).ExecuteAsync(httpContext);
        }
    }

    /// <summary>Resposta gravada pela idempotência (status e corpo exatos), com <c>Location</c> quando criou.</summary>
    private sealed class StoredResponseResult(int status, string body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            if (status == StatusCodes.Status201Created)
            {
                using var document = JsonDocument.Parse(body);
                var id = document.RootElement.GetProperty("id").GetGuid();
                httpContext.Response.Headers.Location = $"{BasePath}/{id}";
            }

            httpContext.Response.Headers.CacheControl = "no-store";
            return TypedResults.Text(body, MediaTypeNames.Application.Json, Encoding.UTF8, status).ExecuteAsync(httpContext);
        }
    }
}
