using Applada.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Applada.Modules.Identity.Api;

/// <summary>
/// Depois da autenticação, mapeia (iss, sub) do token para o usuário interno, uma vez por requisição.
/// Token autenticado sem <c>iss</c> ou <c>sub</c> não serve ao Applada: responde 401.
/// Nunca registra token, iss nem sub em log.
/// </summary>
internal sealed partial class CurrentUserMiddleware(RequestDelegate next, ILogger<CurrentUserMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, UserProvisioner provisioner, HttpCurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            LogMissingIdentityClaims(logger);
            await context.ChallengeAsync();
            return;
        }

        var (userId, created) = await provisioner.EnsureUserAsync(issuer, subject, context.RequestAborted);
        if (created)
        {
            LogUserRegistered(logger, userId);
        }

        currentUser.Resolve(userId);
        await next(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token autenticado sem claim iss ou sub; requisição rejeitada com 401.")]
    private static partial void LogMissingIdentityClaims(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Usuário interno {UserId} criado no primeiro acesso.")]
    private static partial void LogUserRegistered(ILogger logger, Guid userId);
}
