using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Applada.Api.IntegrationTests.Infrastructure;

public static class TestJwt
{
    public const string Issuer = "https://test-issuer.applada.local";
    public const string Audience = "applada-api";

    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string Issue(
        string subject,
        string? issuer = null,
        string? audience = null,
        DateTimeOffset? expires = null,
        SecurityKey? key = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = (expires ?? DateTimeOffset.UtcNow.AddHours(1)).UtcDateTime,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
