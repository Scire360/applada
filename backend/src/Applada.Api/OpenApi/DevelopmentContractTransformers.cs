using Applada.Modules.Discovery.Api;
using Applada.Modules.Matches.Api;
using Applada.Modules.Matches.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Applada.Api.OpenApi;

/// <summary>
/// Complementa o documento OpenAPI (só Development) com o que os handlers leem à mão e, por isso, a inferência do
/// framework não enxerga: corpos JSON, cabeçalhos obrigatórios e descrição dos parâmetros de consulta. Só descreve; nada
/// aqui altera o comportamento das rotas.
/// </summary>
internal static class DevelopmentContractTransformers
{
    private const string JsonMedia = "application/json";
    private const string PrivateNoStore = "private, no-store: a resposta depende de quem pergunta.";
    private const string ETagDescription = "ETag fraco W/\"<version>\" da versão dos dados editáveis (não do corpo); use em If-Match.";

    public static Task DescribeAttendanceStatusAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        if ((type == typeof(RecordAttendanceRequest) || type == typeof(AttendanceResponse) || type == typeof(AttendanceEntryResponse))
            && schema.Properties?.TryGetValue("status", out var property) == true && property is OpenApiSchema status)
        {
            status.Enum = [AttendanceStatusText.Present, AttendanceStatusText.Absent];
            if (type == typeof(AttendanceEntryResponse))
            {
                status.Enum.Add(null!); // Vale JSON null enquanto o organizador não marcar a presença.
            }
        }

        return Task.CompletedTask;
    }

    public static Task DescribeVenueRequestAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (context.JsonTypeInfo.Type == typeof(CreateVenueRequest) && schema.Properties is { } fields)
        {
            schema.Required = new HashSet<string>(["name", "latitude", "longitude"], StringComparer.Ordinal);
            if (fields["name"] is OpenApiSchema name)
            {
                name.Type &= ~JsonSchemaType.Null;
            }

            if (fields["latitude"] is OpenApiSchema latitude && fields["longitude"] is OpenApiSchema longitude)
            {
                latitude.Type &= ~JsonSchemaType.Null;
                longitude.Type &= ~JsonSchemaType.Null;
                latitude.Minimum = "-90";
                latitude.Maximum = "90";
                longitude.Minimum = "-180";
                longitude.Maximum = "180";
            }
        }

        return Task.CompletedTask;
    }

    public static async Task TransformOperationAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        switch (operation.OperationId)
        {
            case "CreateVenue":
                AddResponseHeader(operation, "201", "Location", "URL do local cadastrado (/api/v1/venues/{id}).");
                break;
            case "DiscoverMatches":
                Describe(operation, "lat", "Latitude entre -90 e 90, arredondada a duas casas na busca.", required: true);
                Describe(operation, "lon", "Longitude entre -180 e 180, arredondada a duas casas na busca.", required: true);
                Describe(operation, "radiusKm", "Raio em quilômetros, inteiro de 1 a 50 (padrão 10).");
                goto case "ListMyVenues";
            case "ListMyVenues":
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                Describe(operation, "cursor", "Cursor opaco devolvido em nextCursor; só vale para esta listagem.");
                Describe(operation, "limit", "Itens por página, de 1 a 50 (padrão 20).");
                break;
            case "CreateMatch":
                AddHeader(operation, "Idempotency-Key", required: true,
                    "Chave de idempotência (1 a 100 caracteres ASCII imprimíveis), por usuário, retida por 24 h. Mesma chave com o mesmo corpo devolve a resposta gravada; com corpo diferente, 422.");
                var createSchema = await AddJsonBodyAsync<CreateMatchRequest>(operation, context, ct);
                if (createSchema.Properties?.TryGetValue("startsAt", out var property) == true && property is OpenApiSchema startsAt)
                {
                    startsAt.Type = JsonSchemaType.String;
                    startsAt.Format = "date-time";
                    startsAt.Description = "Data e hora ISO 8601 com Z ou offset explícito.";
                }

                AddResponseHeader(operation, "201", "Location", "URL da partida criada (/api/v1/matches/{id}).");
                AddResponseHeader(operation, "201", "Cache-Control", "no-store");
                break;
            case "UpdateMatch":
                AddHeader(operation, "If-Match", required: true,
                    "ETag fraco da partida (W/\"<version>\") lido no GET; ausente é 428, divergente é 412.");
                await AddJsonBodyAsync<UpdateMatchRequest>(operation, context, ct);
                AddResponseHeader(operation, "200", "ETag", ETagDescription);
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                break;
            case "GetMatch":
            case "CancelMatch":
                AddResponseHeader(operation, "200", "ETag", ETagDescription);
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                break;
            case "Register":
                AddResponseHeader(operation, "201", "Cache-Control", PrivateNoStore);
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                break;
            case "ListMyMatches":
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                Describe(operation, "role", "Obrigatório: organizer (partidas que organizo, futuras ou com chamada aberta) ou participant (próximas partidas com inscrição ativa).", required: true, ["organizer", "participant"]);
                Describe(operation, "cursor", "Cursor opaco devolvido em nextCursor; só vale para esta listagem.");
                Describe(operation, "limit", "Itens por página, de 1 a 50 (padrão 20).");
                break;
            case "RecordAttendance":
            case "GetMyProfile":
            case "SetMyDisplayName":
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                break;
            case "ListAttendance":
            case "ListParticipants":
                AddResponseHeader(operation, "200", "Cache-Control", PrivateNoStore);
                Describe(operation, "cursor", "Cursor opaco devolvido em nextCursor; só vale para esta listagem.");
                Describe(operation, "limit", "Itens por página, de 1 a 50 (padrão 20).");
                break;
            default:
                break;
        }
    }

    /// <summary>Rotas de saúde (anônimas) são mapeadas por <c>MapHealthChecks</c>, que o gerador não descreve.</summary>
    public static Task TransformDocumentAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Paths ??= [];
        document.Paths["/health/live"] = HealthPath("HealthLive", "Liveness: o processo responde (não consulta dependências).", withUnavailable: false);
        document.Paths["/health/ready"] = HealthPath(
            "HealthReady",
            "Readiness: PostgreSQL e RabbitMQ. RabbitMQ fora do ar aparece como Degraded com HTTP 200; PostgreSQL fora do ar é 503.",
            withUnavailable: true);
        return Task.CompletedTask;
    }

    private static OpenApiPathItem HealthPath(string operationId, string summary, bool withUnavailable)
    {
        var responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "Healthy ou Degraded (texto simples)." },
        };
        if (withUnavailable)
        {
            responses["503"] = new OpenApiResponse { Description = "Unhealthy (texto simples)." };
        }

        return new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new OpenApiOperation
                {
                    OperationId = operationId,
                    Summary = summary,
                    Tags = new HashSet<OpenApiTagReference>(),
                    Security = [],
                    Responses = responses,
                },
            },
        };
    }

    private static void AddResponseHeader(OpenApiOperation operation, string status, string name, string description)
    {
        if (operation.Responses?[status] is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers[name] = new OpenApiHeader { Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        }
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }

    private static async Task<IOpenApiSchema> AddJsonBodyAsync<T>(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var schema = await context.GetOrCreateSchemaAsync(typeof(T), parameterDescription: null, ct);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType> { [JsonMedia] = new OpenApiMediaType { Schema = schema } },
        };
        return schema;
    }

    private static void Describe(OpenApiOperation operation, string name, string description, bool required = false, string[]? allowed = null)
    {
        var parameter = operation.Parameters?.OfType<OpenApiParameter>().FirstOrDefault(p => p.Name == name);
        if (parameter is null)
        {
            return;
        }

        parameter.Description = description;
        parameter.Required = required;
        if (allowed is not null && parameter.Schema is OpenApiSchema schema)
        {
            schema.Enum = [.. allowed.Select(a => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(a)!)];
        }
    }
}
