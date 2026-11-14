using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Identity.Infrastructure;

/// <summary>
/// Garante um usuário interno por (issuer, subject). Seguro sob concorrência: a constraint
/// UNIQUE decide quem cria, e quem perde a corrida lê o id já gravado.
/// </summary>
public sealed class UserProvisioner(IdentityDbContext db, TimeProvider timeProvider)
{
    public async Task<(Guid UserId, bool Created)> EnsureUserAsync(string issuer, string subject, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();

        // Sem composição LINQ: INSERT ... RETURNING não pode virar subconsulta.
        var inserted = await db.Database
            .SqlQuery<Guid>($"""
                INSERT INTO "identity"."users" (id, issuer, subject, created_at)
                VALUES ({id}, {issuer}, {subject}, {now})
                ON CONFLICT (issuer, subject) DO NOTHING
                RETURNING id AS "Value"
                """)
            .ToListAsync(ct);

        if (inserted.Count == 1)
        {
            return (inserted[0], true);
        }

        // Conflito: a linha já existe (ou acabou de ser confirmada por outra requisição).
        var existing = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM "identity"."users"
                WHERE issuer = {issuer} AND subject = {subject}
                """)
            .ToListAsync(ct);

        return (existing.Single(), false);
    }
}
