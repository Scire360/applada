using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Communities.Domain;

/// <summary>Erros do módulo. O código vira o <c>type</c> do problem+json (<c>urn:applada:problem:&lt;code&gt;</c>).</summary>
internal static class CommunityErrors
{
    /// <summary>Grupo inexistente ou o usuário não é membro: a mesma resposta, para não revelar que o grupo existe.</summary>
    public static Error GroupNotFound { get; } = new("group-not-found", ErrorKind.NotFound, "Grupo não encontrado.");

    public static Error Forbidden { get; } = new("group-forbidden", ErrorKind.Forbidden, "Seu papel no grupo não permite esta ação.");

    public static Error MemberNotFound { get; } = new("member-not-found", ErrorKind.NotFound, "Membro não encontrado no grupo.");

    public static Error InvitationNotFound { get; } = new("invitation-not-found", ErrorKind.NotFound, "Convite não encontrado.");

    /// <summary>Token desconhecido, expirado, revogado ou esgotado: o motivo não é revelado.</summary>
    public static Error InvitationInvalid { get; } = new("invitation-invalid", ErrorKind.NotFound, "Convite inválido ou expirado.");

    public static Error GroupFull { get; } = new("group-full", ErrorKind.Conflict, $"O grupo atingiu o limite de {GroupRules.MaxMembers} membros.");

    public static Error OwnerCannotLeave { get; } = new("owner-cannot-leave", ErrorKind.Conflict, "O dono não pode sair do grupo.");

    public static Error OwnerRoleFixed { get; } = new("owner-role-fixed", ErrorKind.Conflict, "O papel do dono não pode ser alterado.");

    public static Error Validation(string field, string message) =>
        new("validation", ErrorKind.Validation, "Dados inválidos.", new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
