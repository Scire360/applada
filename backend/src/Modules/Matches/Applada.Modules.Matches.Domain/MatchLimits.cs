namespace Applada.Modules.Matches.Domain;

/// <summary>Limites das regras de partida (spec §7 e §8). As constraints do banco usam os mesmos valores.</summary>
public static class MatchLimits
{
    public const int TitleMax = 100;
    public const int LocationTextMax = 200;
    public const int DurationMin = 15;
    public const int DurationMax = 600;
    public const int CapacityMin = 1;
    public const int CapacityMax = 200;
    public const int WaitlistMax = 50;
    public const int MaxDaysAhead = 365;
}
