using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Identity.Infrastructure;

/// <summary>
/// Garante um usuário interno por (issuer, subject). Seguro sob concorrência: a constraint
/// UNIQUE decide quem cria, e quem perde a corrida lê o id já gravado. Quem cria grava
/// <see cref="UserRegisteredV1"/> no outbox na mesma transação do INSERT: ou os dois existem, ou nenhum.
/// </summary>
public sealed class UserProvisioner(IdentityDbContext db, TimeProvider timeProvider)
{
    public async Task<(Guid UserId, bool Created)> EnsureUserAsync(string issuer, string subject, string correlationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

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
            db.OutboxMessages.Add(OutboxMessage.From(new UserRegisteredV1(id), correlationId, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (id, true);
        }

        // Conflito: a linha já existe (ou acabou de ser confirmada por outra requisição). Em READ
        // COMMITTED cada comando enxerga o que já foi confirmado, então a leitura na mesma transação acha a linha.
        var existing = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM "identity"."users"
                WHERE issuer = {issuer} AND subject = {subject}
                """)
            .ToListAsync(ct);

        await transaction.CommitAsync(ct);
        return (existing.Single(), false);
    }
}
