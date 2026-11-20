using Applada.BuildingBlocks.Domain;
using Applada.Modules.Communities.Contracts;

namespace Applada.Modules.Communities.Domain;

/// <summary>Limites e validação de entrada do grupo e do convite.</summary>
internal static class GroupRules
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;
    public const int MaxMembers = 500;
    public const int DefaultInvitationHours = 7 * 24;
    public const int MaxInvitationHours = 30 * 24;
    public const int DefaultInvitationUses = 50;
    public const int MaxInvitationUses = MaxMembers;

    /// <summary>Remove espaços nas pontas; nulo se vazio, longo demais ou com caractere de controle.</summary>
    public static string? NormalizeName(string? input)
    {
        var name = input?.Trim();
        return name is { Length: > 0 and <= NameMaxLength } && !name.Any(char.IsControl) ? name : null;
    }

    /// <summary>Descrição opcional: vazia vira nulo. Quebra de linha é aceita; outros caracteres de controle não.</summary>
    public static Result<string?> NormalizeDescription(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (string?)null;
        }

        return text.Length <= DescriptionMaxLength && !text.Any(c => char.IsControl(c) && c is not '\n' and not '\r')
            ? text
            : CommunityErrors.Validation("description", $"A descrição deve ter até {DescriptionMaxLength} caracteres, sem caracteres de controle.");
    }

    public static string ToText(GroupRole role) => role switch
    {
        GroupRole.Owner => "owner",
        GroupRole.Admin => "admin",
        GroupRole.Member => "member",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel desconhecido."),
    };

    public static GroupRole FromText(string text) => text switch
    {
        "owner" => GroupRole.Owner,
        "admin" => GroupRole.Admin,
        "member" => GroupRole.Member,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Papel desconhecido."),
    };
}

/// <summary>Quem pode fazer o quê num grupo. Recebe papéis já carregados sob o lock do grupo.</summary>
internal static class GroupPolicy
{
    public static bool CanInvite(GroupRole actor) => actor is GroupRole.Owner or GroupRole.Admin;

    /// <summary>Só o owner muda papéis, e nunca o próprio papel de owner.</summary>
    public static Error? CanChangeRole(GroupRole actor, GroupRole target) =>
        actor != GroupRole.Owner ? CommunityErrors.Forbidden
        : target == GroupRole.Owner ? CommunityErrors.OwnerRoleFixed
        : null;

    /// <summary>
    /// Sair: qualquer um, menos o owner. Remover outro: owner remove admin e member; admin remove member.
    /// </summary>
    public static Error? CanRemove(GroupRole actor, bool self, GroupRole target)
    {
        if (self)
        {
            return target == GroupRole.Owner ? CommunityErrors.OwnerCannotLeave : null;
        }

        return (actor, target) switch
        {
            (GroupRole.Owner, not GroupRole.Owner) => null,
            (GroupRole.Admin, GroupRole.Member) => null,
            _ => CommunityErrors.Forbidden,
        };
    }
}
