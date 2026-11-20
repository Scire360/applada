namespace Applada.Modules.Matches.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid OrganizerId = Guid.Parse("0196f0a0-0000-7000-8000-000000000001");

    public static NewMatch ValidNewMatch() => new(
        Title: "Pelada de quinta",
        StartsAt: Now.AddDays(2),
        TimeZone: "America/Campo_Grande",
        DurationMinutes: 90,
        Capacity: 10,
        Visibility: "public",
        LocationText: "Quadra do bairro",
        GroupId: null,
        VenueId: null);

    public static Match CreateMatch(int capacity = 10)
    {
        var result = Match.Create(ValidNewMatch() with { Capacity = capacity }, OrganizerId, Now);
        Assert.True(result.IsSuccess);
        var match = result.Value;
        match.ClearDomainEvents();
        return match;
    }

    public static Registration Register(Match match, Guid userId, Registration? existing = null)
    {
        var result = match.Register(userId, existing, Now);
        Assert.True(result.IsSuccess);
        return result.Value.Registration;
    }
}
