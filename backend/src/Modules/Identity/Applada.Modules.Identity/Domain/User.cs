namespace Applada.Modules.Identity.Domain;

/// <summary>
/// Usuário interno do Applada, identificado pelo par (issuer, subject) do provedor OIDC.
/// Não guarda PII: o id interno é o que os demais módulos conhecem.
/// </summary>
public sealed class User
{
    public User(Guid id, string issuer, string subject, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Id = id;
        Issuer = issuer;
        Subject = subject;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Issuer { get; private set; }

    public string Subject { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
