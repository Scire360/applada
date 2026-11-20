using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.Modules.Communities.Contracts;
using Applada.Modules.Communities.Domain;
using Applada.Modules.Communities.Infrastructure;
using Applada.Modules.Profiles.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Applada.Modules.Communities.Api;

public sealed record CreateGroupRequest(string? Name, string? Description);

public sealed record GroupResponse(Guid Id, string Name, string? Description, int MemberCount, string MyRole, DateTimeOffset CreatedAt);

public sealed record MemberResponse(Guid UserId, string? DisplayName, string Role, DateTimeOffset JoinedAt);

public sealed record CreateInvitationRequest(int? ExpiresInHours, int? MaxUses);

/// <summary><see cref="Token"/> aparece só nesta resposta; o servidor guarda apenas o hash.</summary>
public sealed record InvitationResponse(Guid Id, string Token, DateTimeOffset ExpiresAt, int MaxUses);

public sealed record AcceptInvitationRequest(string? Token);

public sealed record AcceptInvitationResponse(Guid GroupId, string Role);

public sealed record ChangeRoleRequest(string? Role);

/// <summary>
/// Rotas do Communities. Todas exigem usuário autenticado. Autorização por recurso: quem não é membro recebe 404
/// (<c>group-not-found</c>) em qualquer rota do grupo, para não revelar que ele existe; membro sem papel suficiente
/// recebe 403 (<c>group-forbidden</c>). Respostas com dados do grupo são <c>private, no-store</c>.
/// </summary>
internal static class GroupEndpoints
{
    private const string GroupsPath = "/api/v1/groups";
    private const string MyGroupsKind = "my-groups";
    private const string MembersKind = "members";
    private const int MaxBodyBytes = 8 * 1024;
    private const int MaxTokenLength = 100;

    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var groups = endpoints.MapGroup(GroupsPath).WithTags("Communities").RequireAuthorization();

        groups.MapPost(string.Empty, CreateAsync).WithName("CreateGroup").RequireRateLimiting(RateLimitPolicies.Writes)
            .Accepts<CreateGroupRequest>("application/json")
            .Produces<GroupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        groups.MapGet("/{id:guid}", GetAsync).WithName("GetGroup")
            .Produces<GroupResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        groups.MapGet("/{id:guid}/members", MembersAsync).WithName("ListGroupMembers")
            .Produces<GroupPage<MemberResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        groups.MapPost("/{id:guid}/invitations", InviteAsync).WithName("CreateGroupInvitation").RequireRateLimiting(RateLimitPolicies.Writes)
            .Accepts<CreateInvitationRequest>("application/json")
            .Produces<InvitationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        groups.MapDelete("/{id:guid}/invitations/{invitationId:guid}", RevokeAsync).WithName("RevokeGroupInvitation")
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        groups.MapPatch("/{id:guid}/members/{userId:guid}", ChangeRoleAsync).WithName("ChangeGroupMemberRole")
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .Accepts<ChangeRoleRequest>("application/json")
            .Produces<MemberResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        groups.MapDelete("/{id:guid}/members/{userId:guid}", RemoveAsync).WithName("RemoveGroupMember")
            .RequireRateLimiting(RateLimitPolicies.Writes)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        endpoints.MapGet("/api/v1/me/groups", MineAsync).WithTags("Communities").WithName("ListMyGroups").RequireAuthorization()
            .Produces<GroupPage<GroupResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        endpoints.MapPost("/api/v1/group-invitations/acceptance", AcceptAsync).WithTags("Communities").WithName("AcceptGroupInvitation")
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Writes)
            .Accepts<AcceptInvitationRequest>("application/json")
            .Produces<AcceptInvitationResponse>(StatusCodes.Status201Created)
            .Produces<AcceptInvitationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context, ICurrentUser currentUser, GroupStore store, TimeProvider time, CancellationToken ct)
    {
        var (body, failure) = await ReadAsync<CreateGroupRequest>(context.Request, ct);
        if (failure is not null)
        {
            return failure;
        }

        var name = GroupRules.NormalizeName(body!.Name);
        if (name is null)
        {
            return Problems.From(CommunityErrors.Validation(
                "name", $"Informe um nome de 1 a {GroupRules.NameMaxLength} caracteres, sem caracteres de controle."));
        }

        var description = GroupRules.NormalizeDescription(body.Description);
        if (!description.IsSuccess)
        {
            return Problems.From(description.Error);
        }

        var correlationId = CorrelationId.Current(fallback: context.TraceIdentifier);
        var group = await store.CreateAsync(currentUser.UserId, name, description.Value, time.GetUtcNow(), correlationId, ct);
        return Problems.Private(ToResponse(group), StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAsync(Guid id, ICurrentUser currentUser, GroupStore store, CancellationToken ct) =>
        await store.FindForMemberAsync(id, currentUser.UserId, ct) is { } group
            ? Problems.Private(ToResponse(group))
            : Problems.From(CommunityErrors.GroupNotFound);

    private static async Task<IResult> MineAsync(
        string? cursor, string? limit, ICurrentUser currentUser, GroupStore store, CancellationToken ct)
    {
        if (!PageCursor.TryParseLimit(limit, out var pageSize))
        {
            return Problems.From(PageCursor.InvalidLimit);
        }

        (DateTimeOffset, Guid)? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, MyGroupsKind, 2, out var keys)
                || !PageCursor.TryParseTime(keys[0], out var joinedAt)
                || !Guid.TryParseExact(keys[1], "N", out var groupId))
            {
                return Problems.From(PageCursor.InvalidCursor);
            }

            after = (joinedAt, groupId);
        }

        var rows = await store.ListMineAsync(currentUser.UserId, after, pageSize + 1, ct);
        var page = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize
            ? PageCursor.Encode(MyGroupsKind, PageCursor.Key(page[^1].JoinedAt), page[^1].Group.Id.ToString("N"))
            : null;
        return Problems.Private(new GroupPage<GroupResponse>([.. page.Select(r => ToResponse(r.Group))], next));
    }

    private static async Task<IResult> MembersAsync(
        Guid id, string? cursor, string? limit, ICurrentUser currentUser, GroupStore store, IProfileNames profiles, CancellationToken ct)
    {
        if (await store.GetRoleAsync(id, currentUser.UserId, ct) is null)
        {
            return Problems.From(CommunityErrors.GroupNotFound);
        }

        if (!PageCursor.TryParseLimit(limit, out var pageSize))
        {
            return Problems.From(PageCursor.InvalidLimit);
        }

        (int, DateTimeOffset, Guid)? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, MembersKind, 3, out var keys)
                || !int.TryParse(keys[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rank)
                || rank is < 0 or > 2
                || !PageCursor.TryParseTime(keys[1], out var joinedAt)
                || !Guid.TryParseExact(keys[2], "N", out var userId))
            {
                return Problems.From(PageCursor.InvalidCursor);
            }

            after = (rank, joinedAt, userId);
        }

        var rows = await store.ListMembersAsync(id, after, pageSize + 1, ct);
        var page = rows.Take(pageSize).ToList();
        var names = await profiles.ListAsync([.. page.Select(m => m.UserId)], ct);
        var next = rows.Count > pageSize
            ? PageCursor.Encode(
                MembersKind,
                GroupStore.RankOf(page[^1].Role).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PageCursor.Key(page[^1].JoinedAt),
                page[^1].UserId.ToString("N"))
            : null;
        var items = page
            .Select(m => new MemberResponse(m.UserId, names.GetValueOrDefault(m.UserId), GroupRules.ToText(m.Role), m.JoinedAt))
            .ToList();
        return Problems.Private(new GroupPage<MemberResponse>(items, next));
    }

    private static async Task<IResult> InviteAsync(
        Guid id, HttpContext context, ICurrentUser currentUser, GroupStore store, TimeProvider time, CancellationToken ct)
    {
        var (body, failure) = await ReadAsync<CreateInvitationRequest>(context.Request, ct);
        if (failure is not null)
        {
            return failure;
        }

        var hours = body!.ExpiresInHours ?? GroupRules.DefaultInvitationHours;
        if (hours is < 1 or > GroupRules.MaxInvitationHours)
        {
            return Problems.From(CommunityErrors.Validation("expiresInHours", $"A validade deve ficar entre 1 e {GroupRules.MaxInvitationHours} horas."));
        }

        var maxUses = body.MaxUses ?? GroupRules.DefaultInvitationUses;
        if (maxUses is < 1 or > GroupRules.MaxInvitationUses)
        {
            return Problems.From(CommunityErrors.Validation("maxUses", $"O número de usos deve ficar entre 1 e {GroupRules.MaxInvitationUses}."));
        }

        var created = await store.CreateInvitationAsync(id, currentUser.UserId, hours, maxUses, time.GetUtcNow(), ct);
        return created.IsSuccess
            ? Problems.Private(new InvitationResponse(created.Value.Id, created.Value.Token, created.Value.ExpiresAt, created.Value.MaxUses), StatusCodes.Status201Created)
            : Problems.From(created.Error);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id, Guid invitationId, ICurrentUser currentUser, GroupStore store, TimeProvider time, CancellationToken ct)
    {
        var result = await store.RevokeInvitationAsync(id, invitationId, currentUser.UserId, time.GetUtcNow(), ct);
        return result.IsSuccess ? TypedResults.NoContent() : Problems.From(result.Error);
    }

    private static async Task<IResult> AcceptAsync(
        HttpContext context, ICurrentUser currentUser, GroupStore store, TimeProvider time, CancellationToken ct)
    {
        var (body, failure) = await ReadAsync<AcceptInvitationRequest>(context.Request, ct);
        if (failure is not null)
        {
            return failure;
        }

        var token = body!.Token;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return Problems.From(CommunityErrors.InvitationInvalid);
        }

        var correlationId = CorrelationId.Current(fallback: context.TraceIdentifier);
        var (outcome, groupId, role) = await store.AcceptAsync(token, currentUser.UserId, time.GetUtcNow(), correlationId, ct);
        return outcome switch
        {
            AcceptOutcome.Joined => Problems.Private(new AcceptInvitationResponse(groupId, GroupRules.ToText(role)), StatusCodes.Status201Created),
            AcceptOutcome.AlreadyMember => Problems.Private(new AcceptInvitationResponse(groupId, GroupRules.ToText(role))),
            AcceptOutcome.Full => Problems.From(CommunityErrors.GroupFull),
            _ => Problems.From(CommunityErrors.InvitationInvalid),
        };
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid id, Guid userId, HttpContext context, ICurrentUser currentUser, GroupStore store, IProfileNames profiles, CancellationToken ct)
    {
        var (body, failure) = await ReadAsync<ChangeRoleRequest>(context.Request, ct);
        if (failure is not null)
        {
            return failure;
        }

        GroupRole? role = body!.Role switch
        {
            "admin" => GroupRole.Admin,
            "member" => GroupRole.Member,
            _ => null,
        };
        if (role is null)
        {
            return Problems.From(CommunityErrors.Validation("role", "O papel deve ser \"admin\" ou \"member\"."));
        }

        var changed = await store.ChangeRoleAsync(id, currentUser.UserId, userId, role.Value, ct);
        if (!changed.IsSuccess)
        {
            return Problems.From(changed.Error);
        }

        var names = await profiles.ListAsync([userId], ct);
        var member = changed.Value;
        return Problems.Private(new MemberResponse(member.UserId, names.GetValueOrDefault(member.UserId), GroupRules.ToText(member.Role), member.JoinedAt));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, Guid userId, ICurrentUser currentUser, GroupStore store, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, currentUser.UserId, userId, ct);
        return result.IsSuccess ? TypedResults.NoContent() : Problems.From(result.Error);
    }

    private static GroupResponse ToResponse(GroupView group) =>
        new(group.Id, group.Name, group.Description, group.MemberCount, GroupRules.ToText(group.MyRole), group.CreatedAt);

    /// <summary>
    /// JSON pequeno e bem formado: 413 acima do limite, 415 sem JSON, 400 com corpo ilegível ou vazio. O limite vale
    /// também sem <c>Content-Length</c> (<c>Transfer-Encoding: chunked</c>): o corpo é lido com teto, nunca inteiro.
    /// </summary>
    private static async Task<(T? Value, IResult? Failure)> ReadAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        if (!request.HasJsonContentType() || request.GetTypedHeaders().ContentType is { Charset.HasValue: true, Encoding: null })
        {
            return (null, Problems.UnsupportedMediaType());
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            return (null, PayloadTooLarge());
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(length), ct)) > 0)
        {
            length += read;
        }

        if (length > MaxBodyBytes)
        {
            return (null, PayloadTooLarge());
        }

        try
        {
            var options = request.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var value = JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), options);
            return value is null
                ? (null, Problems.From(CommunityErrors.Validation("body", "Envie um objeto JSON.")))
                : (value, null);
        }
        catch (JsonException)
        {
            return (null, Problems.From(CommunityErrors.Validation("body", "JSON inválido.")));
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult PayloadTooLarge() => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Corpo da requisição grande demais.",
        type: "urn:applada:problem:payload-too-large");
}
