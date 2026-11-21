using Applada.Api.Security;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Applada.Api.OpenApi;

/// <summary>
/// Documento OpenAPI e Scalar, <b>somente em Development</b>. Fora dele nada é registrado nem mapeado:
/// as rotas não existem e caem no fallback autenticado (401 anônimo, 404 autenticado).
/// </summary>
public static class DevelopmentApiReference
{
    ///<summary>Client público do realm de dev (infra/keycloak/applada-dev-realm.json).</summary>
    public const string ScalarClientId = "applada-dev-scalar";

    private const string BearerScheme = "Bearer";
    private const string OAuth2Scheme = "OAuth2";

    public static WebApplicationBuilder AddApladaDevelopmentOpenApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer(DevelopmentContractTransformers.DescribeAttendanceStatusAsync);
            options.AddOperationTransformer(DevelopmentContractTransformers.TransformOperationAsync);
            options.AddDocumentTransformer(DevelopmentContractTransformers.TransformDocumentAsync);
            options.AddDocumentTransformer((document, context, _) =>
            {
                var authority = context.ApplicationServices
                    .GetRequiredService<IOptions<AuthenticationSettings>>().Value.Authority.TrimEnd('/');

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token JWT emitido pelo provedor OIDC (Authentication:Authority).",
                };
                // Endpoints no formato do Keycloak: o provedor de dev é o realm do AppHost.
                document.Components.SecuritySchemes[OAuth2Scheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description = "Authorization Code + PKCE (S256) no realm de desenvolvimento.",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authority + "/protocol/openid-connect/auth"),
                            TokenUrl = new Uri(authority + "/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string> { ["openid"] = "OpenID Connect" },
                        },
                    },
                };

                // Qualquer um dos dois esquemas autentica (fallback da API exige usuário autenticado).
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] },
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(OAuth2Scheme, document)] = ["openid"] },
                ];

                return Task.CompletedTask;
            });
        });

        return builder;
    }

    public static WebApplication MapApladaDevelopmentApiReference(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        // Anônimos só em Development: o Scalar carrega o documento e a página antes do login.
        // Padrão do framework: /openapi/{documentName}.json, ou seja, /openapi/v1.json.
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(options => options
                .WithTitle("Applada API (dev)")
                .AddPreferredSecuritySchemes(OAuth2Scheme)
                .AddAuthorizationCodeFlow(OAuth2Scheme, flow =>
                {
                    flow.ClientId = ScalarClientId;
                    flow.Pkce = Pkce.Sha256;
                    flow.SelectedScopes = ["openid"];
                }))
            .AllowAnonymous();

        return app;
    }
}
