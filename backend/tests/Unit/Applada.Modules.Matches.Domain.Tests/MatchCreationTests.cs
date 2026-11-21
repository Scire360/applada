using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain.Events;
using static Applada.Modules.Matches.Domain.Tests.TestData;

namespace Applada.Modules.Matches.Domain.Tests;

public sealed class MatchCreationTests
{
    [Fact]
    public void Create_valid_match_emits_MatchCreated_and_starts_scheduled_with_version_1()
    {
        var data = ValidNewMatch();

        var result = Match.Create(data, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        var match = result.Value;
        Assert.NotEqual(Guid.Empty, match.Id);
        Assert.Equal(OrganizerId, match.OrganizerId);
        Assert.Equal(MatchStatus.Scheduled, match.Status);
        Assert.Equal(1, match.Version);
        Assert.Equal(1, match.NextQueueSeq);
        Assert.Equal(0, match.ConfirmedCount);
        Assert.Equal(0, match.WaitlistCount);
        Assert.Equal(data.Title, match.Title);
        Assert.Equal(data.TimeZone, match.TimeZone);
        Assert.Equal(data.DurationMinutes, match.DurationMinutes);
        Assert.Equal(data.Capacity, match.Capacity);
        Assert.Equal(MatchVisibility.Public, match.Visibility);
        Assert.Equal(data.LocationText, match.LocationText);
        Assert.Equal(Now, match.CreatedAt);
        Assert.Equal(Now, match.UpdatedAt);
        Assert.Null(match.CancelledAt);

        var created = Assert.IsType<MatchCreated>(Assert.Single(match.DomainEvents));
        Assert.Equal(new MatchCreated(match.Id, OrganizerId, match.StartsAt, data.Capacity, MatchVisibility.Public), created);
    }

    public static TheoryData<string, NewMatch> InvalidFields() => new()
    {
        { "title", ValidNewMatch() with { Title = "   " } },
        { "title", ValidNewMatch() with { Title = new string('a', 101) } },
        { "durationMinutes", ValidNewMatch() with { DurationMinutes = 14 } },
        { "durationMinutes", ValidNewMatch() with { DurationMinutes = 601 } },
        { "capacity", ValidNewMatch() with { Capacity = 0 } },
        { "capacity", ValidNewMatch() with { Capacity = 201 } },
        { "timeZone", ValidNewMatch() with { TimeZone = "Mars/Base" } },
        { "visibility", ValidNewMatch() with { Visibility = "private" } },
        { "locationText", ValidNewMatch() with { LocationText = new string('a', 201) } },
        { "timeZone", ValidNewMatch() with { TimeZone = "E. South America Standard Time" } },
        { "title", ValidNewMatch() with { Title = "Pelada\u0000" } },
        { "title", ValidNewMatch() with { Title = "Pelada \u0007 de quinta" } },
        { "locationText", ValidNewMatch() with { LocationText = "\u0000" } },
        { "locationText", ValidNewMatch() with { LocationText = "Quadra\u0007 do bairro" } },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Create_rejects_invalid_field(string field, NewMatch data)
    {
        var result = Match.Create(data, OrganizerId, Now);

        AssertValidation(result, field);
    }

    [Fact]
    public void Create_reports_every_invalid_field_at_once()
    {
        var data = ValidNewMatch() with { Title = "", Capacity = 0, TimeZone = "Mars/Base" };

        var result = Match.Create(data, OrganizerId, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("Dados inválidos.", result.Error.Message);
        Assert.Equal(["capacity", "timeZone", "title"], result.Error.Fields!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Create_accepts_title_of_100_accented_chars()
    {
        var title = new string('ã', 100);

        var result = Match.Create(ValidNewMatch() with { Title = title }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(title, result.Value.Title);
    }

    [Fact]
    public void Create_trims_title()
    {
        var result = Match.Create(ValidNewMatch() with { Title = "  Racha do sábado  " }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Racha do sábado", result.Value.Title);
    }

    [Fact]
    public void Create_stores_starts_at_in_utc()
    {
        var startsAt = new DateTimeOffset(2026, 11, 20, 19, 30, 0, TimeSpan.FromHours(-4));

        var result = Match.Create(ValidNewMatch() with { StartsAt = startsAt }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, result.Value.StartsAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero), result.Value.StartsAt);
        Assert.Equal(TimeSpan.Zero, Assert.IsType<MatchCreated>(Assert.Single(result.Value.DomainEvents)).StartsAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_turns_blank_location_into_null(string locationText)
    {
        var result = Match.Create(ValidNewMatch() with { LocationText = locationText }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LocationText);
    }

    [Fact]
    public void StartsAt_one_second_in_past_is_invalid()
    {
        var result = Match.Create(ValidNewMatch() with { StartsAt = Now.AddSeconds(-1) }, OrganizerId, Now);

        AssertValidation(result, "startsAt");
    }

    [Fact]
    public void StartsAt_equal_to_now_is_invalid()
    {
        var result = Match.Create(ValidNewMatch() with { StartsAt = Now }, OrganizerId, Now);

        AssertValidation(result, "startsAt");
    }

    [Fact]
    public void StartsAt_exactly_365_days_ahead_is_valid()
    {
        var result = Match.Create(ValidNewMatch() with { StartsAt = Now.AddDays(365) }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void StartsAt_365_days_plus_one_second_is_invalid()
    {
        var result = Match.Create(ValidNewMatch() with { StartsAt = Now.AddDays(365).AddSeconds(1) }, OrganizerId, Now);

        AssertValidation(result, "startsAt");
    }

    [Theory]
    [InlineData("America/Campo_Grande")]
    [InlineData("America/Sao_Paulo")]
    public void Accepts_America_Campo_Grande_and_America_Sao_Paulo(string timeZone)
    {
        var result = Match.Create(ValidNewMatch() with { TimeZone = timeZone }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(timeZone, result.Value.TimeZone);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Etc/UTC")]
    public void Accepts_UTC_and_Etc_UTC(string timeZone)
    {
        var result = Match.Create(ValidNewMatch() with { TimeZone = timeZone }, OrganizerId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(timeZone, result.Value.TimeZone);
    }

    [Fact]
    public void Time_zone_with_non_canonical_casing_is_rejected()
    {
        // No Windows, depois que America/Sao_Paulo entra no cache do TimeZoneInfo, "america/sao_paulo" passa a ser
        // achado (o cache ignora caixa); antes disso, e no Linux, não. Aquecer o cache aqui reproduz o pior caso;
        // exigir o id canônico exato deixa o resultado igual em qualquer sistema e estado de cache.
        _ = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var result = Match.Create(ValidNewMatch() with { TimeZone = "america/sao_paulo" }, OrganizerId, Now);

        AssertValidation(result, "timeZone");
    }

    private static void AssertValidation(Result<Match> result, string field)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.NotNull(result.Error.Fields);
        Assert.True(result.Error.Fields.ContainsKey(field), $"Fields sem a chave '{field}': {string.Join(", ", result.Error.Fields.Keys)}");
    }
}
