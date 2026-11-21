using System.Globalization;
using System.Text;
using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Discovery.Domain;
using Applada.Modules.Discovery.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Applada.Modules.Discovery.Api;

public sealed record CreateVenueRequest(string? Name, string? AddressText, double? Latitude, double? Longitude);
public sealed record VenueResponse(Guid Id, string Name, string? AddressText, double Latitude, double Longitude);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

internal static class VenueEndpoints
{
    private const string VenuesPath = "/api/v1/venues";
    private const int MaxBodyBytes = 8192;
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var venues = endpoints.MapGroup(VenuesPath).WithTags("Discovery").RequireAuthorization();
        venues.MapPost(string.Empty, CreateAsync).WithName("CreateVenue").RequireRateLimiting(RateLimitPolicies.Writes)
            .Accepts<CreateVenueRequest>("application/json")
            .Produces<VenueResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        venues.MapGet("/{id:guid}", GetAsync).WithName("GetVenue")
            .Produces<VenueResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        endpoints.MapGet("/api/v1/me/venues", ListMineAsync).WithTags("Discovery").WithName("ListMyVenues").RequireAuthorization()
            .Produces<Page<VenueResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, ICurrentUser user, VenueStore store, TimeProvider clock, IOptions<JsonOptions> json, CancellationToken ct)
    {
        if (!request.HasJsonContentType())
        {
            return UnsupportedMediaType();
        }

        var mediaType = request.GetTypedHeaders().ContentType;
        if (mediaType is { Charset.HasValue: true, Encoding: null })
        {
            return UnsupportedMediaType();
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return PayloadTooLarge();
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var length = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        if (length > MaxBodyBytes)
        {
            return PayloadTooLarge();
        }

        CreateVenueRequest? body;
        try
        {
            using var stream = new MemoryStream(buffer, 0, length, writable: false);
            using var utf8 = mediaType?.Encoding is { } encoding && !encoding.Equals(Encoding.UTF8)
                ? Encoding.CreateTranscodingStream(stream, encoding, Encoding.UTF8) : null;
            body = await JsonSerializer.DeserializeAsync<CreateVenueRequest>(utf8 ?? stream, json.Value.SerializerOptions, ct);
        }
        catch (JsonException)
        {
            return Invalid("body", "Envie um objeto JSON válido.");
        }

        if (body is null)
        {
            return Invalid("body", "Envie um objeto JSON.");
        }

        var result = Venue.Create(body.Name, body.AddressText, body.Latitude, body.Longitude, user.UserId, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return TypedResults.ValidationProblem(result.Error.Fields!.ToDictionary(StringComparer.Ordinal),
                title: result.Error.Message, type: "urn:applada:problem:validation");
        }

        var venue = result.Value;
        await store.AddAsync(venue, ct);
        return TypedResults.Created($"{VenuesPath}/{venue.Id}", ToResponse(venue));
    }

    private static async Task<IResult> GetAsync(Guid id, VenueStore store, CancellationToken ct) =>
        await store.FindAsync(id, ct) is { } venue
            ? TypedResults.Ok(ToResponse(venue))
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Local não encontrado.", type: "urn:applada:problem:venue-not-found");

    private static async Task<IResult> ListMineAsync(
        string? cursor, string? limit, HttpResponse response, ICurrentUser user, VenueStore store, CancellationToken ct)
    {
        var pageSize = DefaultLimit;
        if (!string.IsNullOrEmpty(limit) && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
            || pageSize is < 1 or > MaxLimit))
        {
            return Invalid("limit", $"O limite deve ser um inteiro entre 1 e {MaxLimit}.");
        }

        if (!VenueCursor.TryParse(cursor, out var after))
        {
            return Invalid("cursor", "Cursor inválido.");
        }

        var venues = await store.ListMineAsync(user.UserId, after, pageSize, ct);
        var nextCursor = venues.Count > pageSize ? VenueCursor.Encode(venues[pageSize - 1]) : null;
        response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(new Page<VenueResponse>([.. venues.Take(pageSize).Select(ToResponse)], nextCursor));
    }

    private static VenueResponse ToResponse(Venue venue) =>
        new(venue.Id, venue.Name, venue.AddressText, venue.Latitude, venue.Longitude);

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] },
            title: "Dados inválidos.", type: "urn:applada:problem:validation");

    private static ProblemHttpResult PayloadTooLarge() => TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Corpo da requisição grande demais.", type: "urn:applada:problem:payload-too-large");

    private static ProblemHttpResult UnsupportedMediaType() => TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
        title: "O corpo deve ser JSON com uma codificação reconhecida.", type: "urn:applada:problem:unsupported-media-type");
}
