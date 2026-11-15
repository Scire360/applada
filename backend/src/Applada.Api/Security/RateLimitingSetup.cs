using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Applada.BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Applada.Api.Security;

/// <summary>Limite de escritas por usuário (seção <c>Matches</c>).</summary>
public sealed class WriteRateLimitSettings
{
    public const string SectionName = "Matches";

    [Range(1, 100_000)]
    public int WriteRateLimitPerMinute { get; set; } = 30;
}

public static class RateLimitingSetup
{
    private const string RateLimitedType = "urn:applada:problem:rate-limited";

    /// <summary>
    /// Política <see cref="RateLimitPolicies.Writes"/>: janela fixa de 1 minuto por usuário interno (sem usuário, por IP), com
    /// <c>Matches:WriteRateLimitPerMinute</c> permissões (padrão 30). Excedeu: 429 problem+json com <c>Retry-After</c>.
    /// O middleware deve rodar depois da resolução do usuário (<c>UseCurrentUser</c>) e da autorização.
    /// </summary>
    public static IServiceCollection AddApladaRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WriteRateLimitSettings>()
            .Bind(configuration.GetSection(WriteRateLimitSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Writes, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<WriteRateLimitSettings>>().Value.WriteRateLimitPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
            options.OnRejected = (context, _) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = RateLimitedType,
                        Title = "Muitas escritas em pouco tempo; aguarde e tente de novo.",
                        Status = StatusCodes.Status429TooManyRequests,
                    },
                });
            };
        });

        return services;
    }

    /// <summary>
    /// Usuário interno quando resolvido; senão o IP. As rotas limitadas exigem autenticação, então o IP é só defesa.
    /// </summary>
    private static string PartitionKey(HttpContext context)
    {
        try
        {
            return "user:" + context.RequestServices.GetRequiredService<ICurrentUser>().UserId.ToString("N");
        }
        catch (InvalidOperationException)
        {
            return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }
}
