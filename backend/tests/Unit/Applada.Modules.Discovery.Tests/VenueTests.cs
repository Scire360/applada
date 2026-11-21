using Applada.Modules.Discovery.Domain;

namespace Applada.Modules.Discovery.Tests;

public class VenueTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void Name_length_is_validated_after_trim(int length, bool valid)
    {
        var name = new string('a', length);
        var result = Venue.Create($"  {name}  ", null, 0, 0, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Equal(valid, result.IsSuccess);
        if (valid)
        {
            Assert.Equal(name, result.Value.Name);
        }
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Address_length_is_validated(int length, bool valid)
    {
        var address = new string('a', length);
        var result = Venue.Create("Arena", address, 0, 0, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Equal(valid, result.IsSuccess);
    }

    [Theory]
    [InlineData(-90d, -180d, true)]
    [InlineData(90d, 180d, true)]
    [InlineData(-90.0001, 0d, false)]
    [InlineData(90.0001, 0d, false)]
    [InlineData(0d, -180.0001, false)]
    [InlineData(0d, 180.0001, false)]
    [InlineData(double.NaN, 0d, false)]
    [InlineData(0d, double.NaN, false)]
    [InlineData(double.PositiveInfinity, 0d, false)]
    [InlineData(0d, double.NegativeInfinity, false)]
    [InlineData(null, 0d, false)]
    [InlineData(0d, null, false)]
    public void Coordinates_are_required_finite_and_in_range(double? latitude, double? longitude, bool valid)
    {
        var result = Venue.Create("Arena", null, latitude, longitude, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Equal(valid, result.IsSuccess);
    }

    [Fact]
    public void Text_limits_count_unicode_characters_like_postgresql()
    {
        const string stadium = "🏟";
        var name = string.Concat(Enumerable.Repeat(stadium, 80));
        var address = string.Concat(Enumerable.Repeat(stadium, 200));
        Assert.True(Venue.Create(name, address, 0, 0, Guid.NewGuid(), DateTimeOffset.UnixEpoch).IsSuccess);
        Assert.False(Venue.Create("A" + stadium, null, 0, 0, Guid.NewGuid(), DateTimeOffset.UnixEpoch).IsSuccess);
    }

    [Fact]
    public void Coordinates_are_rounded_to_five_places_away_from_zero()
    {
        var result = Venue.Create("Arena", null, -1.234565, 1.234565, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Equal(-1.23457, result.Value.Latitude);
        Assert.Equal(1.23457, result.Value.Longitude);
    }
}
