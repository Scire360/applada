using System.Globalization;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.CancelMatch;
using Applada.Modules.Matches.Application.CreateMatch;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Application.UpdateMatch;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
        var body = await ReadJsonAsync<CreateMatchRequest>(request, json, ct);
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

        var body = await ReadJsonAsync<UpdateMatchRequest>(request, jsonOptions.Value.SerializerOptions, ct);
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

    /// <summary>
    /// Lê o corpo JSON sem deixar erro de formato virar 500: JSON malformado, tipo errado ou texto inválido (como um
    /// surrogate isolado) vira 400 <c>validation</c>, com a chave do campo quando o erro aponta um campo conhecido.
    /// Corpo acima de <see cref="MaxBodyBytes"/> vira 413, com ou sem <c>Content-Length</c>: nunca se lê além do limite.
    /// </summary>
    private static async Task<JsonBody<T>> ReadJsonAsync<T>(HttpRequest request, JsonSerializerOptions options, CancellationToken ct)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return new JsonBody<T>(null, TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "O corpo deve ser application/json."));
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return new JsonBody<T>(null, PayloadTooLarge());
        }

        // Lê no máximo MaxBodyBytes + 1: o byte a mais só serve para saber que passou do limite.
        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length > MaxBodyBytes)
        {
            return new JsonBody<T>(null, PayloadTooLarge());
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), options);
            return value is null
                ? new JsonBody<T>(null, InvalidBody("body"))
                : new JsonBody<T>(value, null);
        }
        catch (JsonException ex)
        {
            return new JsonBody<T>(null, InvalidBody(FieldFromPath<T>(ex.Path, options)));
        }
    }

    private static ProblemHttpResult PayloadTooLarge() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: $"O corpo pode ter no máximo {MaxBodyBytes / 1024} KB.",
            type: "urn:applada:problem:payload-too-large");

    private static IResult InvalidBody(string field) =>
        ProblemResults.From(MatchErrors.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = ["Valor ausente, malformado ou de tipo inválido."],
        }));

    /// <summary>
    /// Campo de primeiro nível do caminho do erro (<c>$.capacity</c> → <c>capacity</c>), só se for uma propriedade do
    /// DTO: o nome nunca ecoa texto arbitrário do cliente. Fora disso, <c>body</c>.
    /// </summary>
    private static string FieldFromPath<T>(string? path, JsonSerializerOptions options)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return "body";
        }

        var segment = path.AsSpan(2);
        var end = segment.IndexOfAny('.', '[');
        if (end >= 0)
        {
            segment = segment[..end];
        }

        foreach (var property in options.GetTypeInfo(typeof(T)).Properties)
        {
            if (segment.Equals(property.Name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Name;
            }
        }

        return "body";
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Contadores da partida {MatchId} divergem das inscrições; edição abortada.")]
    private static partial void LogStateDivergence(ILogger logger, Guid matchId, Exception exception);

    private sealed record JsonBody<T>(T? Value, IResult? Failure);

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
