namespace Applada.Modules.Profiles.Domain;

internal static class DisplayNameRules
{
    public const int MaxLength = 80;

    public static string? Normalize(string? input)
    {
        var name = input?.Trim();
        return name is { Length: > 0 and <= MaxLength } && !name.Any(char.IsControl) ? name : null;
    }
}
