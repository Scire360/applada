using System.Globalization;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Applada.Api.IntegrationTests;

/// <summary>Validação de <c>Matches:LockTimeout</c>, sem banco: só o registro das opções.</summary>
public class MatchesPersistenceOptionsTests
{
    [Theory]
    [InlineData("00:00:00.001")]
    [InlineData("00:00:05")]
    [InlineData("00:01:00")]
    public void Lock_timeout_within_1ms_and_1min_is_accepted(string value)
    {
        using var provider = BuildProvider(value);

        var options = provider.GetRequiredService<IOptions<MatchesPersistenceOptions>>().Value;

        Assert.Equal(TimeSpan.Parse(value, CultureInfo.InvariantCulture), options.LockTimeout);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:01:00.001")]
    [InlineData("01:00:00")]
    public void Lock_timeout_outside_1ms_and_1min_is_rejected(string value)
    {
        using var provider = BuildProvider(value);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MatchesPersistenceOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(string lockTimeout)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Matches:LockTimeout"] = lockTimeout })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMatchesInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
