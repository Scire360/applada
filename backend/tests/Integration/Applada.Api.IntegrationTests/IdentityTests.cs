using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class IdentityTests(ApiFactory factory)
{
    private const string MePath = "/api/v1/me";
    private const string OtherIssuer = "https://outro-issuer-confiavel.applada.local";

    [Fact]
    public async Task Me_returns_same_id_for_same_subject()
    {
        var subject = NewSubject();

        var first = await GetUserIdAsync(factory.CreateClientFor(subject));
        var second = await GetUserIdAsync(factory.CreateClientFor(subject));

        Assert.Equal(first, second);
        Assert.Equal(7, first.Version);
    }

    [Fact]
    public async Task Me_returns_different_ids_for_different_subjects()
    {
        var a = await GetUserIdAsync(factory.CreateClientFor(NewSubject()));
        var b = await GetUserIdAsync(factory.CreateClientFor(NewSubject()));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Same_subject_from_different_issuer_is_different_user()
    {
        // Host derivado que confia em dois issuers; a fixture padrão continua aceitando só TestJwt.Issuer.
        await using var twoIssuers = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters.ValidIssuers = [TestJwt.Issuer, OtherIssuer])));
        var subject = NewSubject();
        var now = factory.Clock.GetUtcNow();

        var fromDefault = await GetUserIdAsync(ClientWith(twoIssuers.CreateClient(), TestJwt.Issue(subject, now: now)));
        var fromOther = await GetUserIdAsync(ClientWith(twoIssuers.CreateClient(), TestJwt.Issue(subject, issuer: OtherIssuer, now: now)));

        Assert.NotEqual(fromDefault, fromOther);
    }

    [Fact]
    public async Task Concurrent_first_requests_create_single_user()
    {
        var subject = NewSubject();
        using var client = factory.CreateClientFor(subject);

        var ids = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => GetUserIdAsync(client, dispose: false)));

        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CountUsersWithSubjectAsync(subject));
    }

    [Fact]
    public async Task Concurrent_provisioning_reports_created_exactly_once()
    {
        // "Created" decide quem grava user-registered no outbox (Tarefa 5): só uma chamada pode recebê-lo.
        var subject = NewSubject();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<UserProvisioner>();
            return await provisioner.EnsureUserAsync(TestJwt.Issuer, subject, TestContext.Current.CancellationToken);
        }));

        Assert.Single(results.Select(r => r.UserId).Distinct());
        Assert.Single(results, r => r.Created);
    }

    [Fact]
    public async Task Token_without_sub_returns_401()
    {
        using var client = ClientWith(factory.CreateClient(), TestJwt.Issue(subject: null, now: factory.Clock.GetUtcNow()));

        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Migrations_are_applied_on_startup_with_history_in_identity_schema()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var applied = await db.Database
            .SqlQuery<string>($"""SELECT "MigrationId" AS "Value" FROM "identity"."__ef_migrations_history" """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(applied, id => id.EndsWith("_InitialIdentity", StringComparison.Ordinal));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private static HttpClient ClientWith(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> GetUserIdAsync(HttpClient client, bool dispose = true)
    {
        try
        {
            var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return body.RootElement.GetProperty("userId").GetGuid();
        }
        finally
        {
            if (dispose)
            {
                client.Dispose();
            }
        }
    }

    private async Task<int> CountUsersWithSubjectAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM "identity"."users" WHERE subject = {subject}""")
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
