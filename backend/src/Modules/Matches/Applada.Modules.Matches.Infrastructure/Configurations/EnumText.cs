using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Applada.Modules.Matches.Infrastructure.Configurations;

/// <summary>
/// Enums gravados como texto minúsculo, exatamente os valores aceitos pelos <c>CHECK</c>s. Mapeamento explícito
/// (não <c>ToString()</c>): renomear um membro do enum não pode mudar o valor no banco em silêncio.
/// </summary>
internal static class EnumText
{
    public static ValueConverter<MatchStatus, string> MatchStatusConverter { get; } =
        new(v => ToText(v), v => ToMatchStatus(v));

    public static ValueConverter<MatchVisibility, string> MatchVisibilityConverter { get; } =
        new(v => ToText(v), v => ToMatchVisibility(v));

    public static ValueConverter<RegistrationStatus, string> RegistrationStatusConverter { get; } =
        new(v => ToText(v), v => ToRegistrationStatus(v));

    private static string ToText(MatchStatus value) => value switch
    {
        MatchStatus.Scheduled => "scheduled",
        MatchStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "MatchStatus sem valor no banco."),
    };

    private static MatchStatus ToMatchStatus(string value) => value switch
    {
        "scheduled" => MatchStatus.Scheduled,
        "cancelled" => MatchStatus.Cancelled,
        _ => throw new InvalidOperationException($"Status de partida desconhecido no banco: '{value}'."),
    };

    private static string ToText(MatchVisibility value) => value switch
    {
        MatchVisibility.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "MatchVisibility sem valor no banco."),
    };

    private static MatchVisibility ToMatchVisibility(string value) => value switch
    {
        "public" => MatchVisibility.Public,
        _ => throw new InvalidOperationException($"Visibilidade desconhecida no banco: '{value}'."),
    };

    private static string ToText(RegistrationStatus value) => value switch
    {
        RegistrationStatus.Confirmed => "confirmed",
        RegistrationStatus.Waitlisted => "waitlisted",
        RegistrationStatus.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "RegistrationStatus sem valor no banco."),
    };

    private static RegistrationStatus ToRegistrationStatus(string value) => value switch
    {
        "confirmed" => RegistrationStatus.Confirmed,
        "waitlisted" => RegistrationStatus.Waitlisted,
        "withdrawn" => RegistrationStatus.Withdrawn,
        _ => throw new InvalidOperationException($"Status de inscrição desconhecido no banco: '{value}'."),
    };
}
