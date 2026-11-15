using System.Collections.Concurrent;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.BuildingBlocks.Messaging;
using Applada.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// Laço do <see cref="OutboxPublisherService{TContext}"/>: as esperas pedidas ao <see cref="TimeProvider"/> são
/// gravadas (e liberadas na hora), então a sequência de backoff é conferida sem depender do relógio real.
/// </summary>
[Collection("api")]
public class OutboxPublisherServiceTests(ApiFactory factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failing_batches_back_off_exponentially_up_to_the_cap_and_log_error_only_once()
    {
        var options = new OutboxOptions { PollInterval = TimeSpan.FromSeconds(1), MaxLoopBackoff = TimeSpan.FromSeconds(30) };
        var scopes = new FailingScopeFactory(factory.Services.GetRequiredService<IServiceScopeFactory>(), failures: int.MaxValue);
        var logs = new CapturingLoggerProvider();
        var clock = new RecordingTimeProvider(factory.Clock);

        await RunUntilAsync(scopes, options, clock, logs, () => clock.Delays.Count >= 7);

        Assert.Equal(
            [2, 4, 8, 16, 30, 30, 30],
            clock.Delays.Take(7).Select(d => d.TotalSeconds));
        var batchLogs = BatchLogs(logs);
        Assert.Equal(LogLevel.Error, batchLogs[0].Level);
        Assert.All(batchLogs.Skip(1), l => Assert.Equal(LogLevel.Warning, l.Level));
        Assert.All(batchLogs, l => Assert.NotNull(l.Exception));
    }

    [Fact]
    public async Task Successful_batch_resets_backoff_to_poll_interval_and_logs_recovery()
    {
        var options = new OutboxOptions { PollInterval = TimeSpan.FromSeconds(1), MaxLoopBackoff = TimeSpan.FromSeconds(30) };
        var scopes = new FailingScopeFactory(factory.Services.GetRequiredService<IServiceScopeFactory>(), failures: 3);
        var logs = new CapturingLoggerProvider();
        var clock = new RecordingTimeProvider(factory.Clock);

        await RunUntilAsync(scopes, options, clock, logs, () => clock.Delays.Count >= 5);

        // Três falhas (2, 4, 8 s); depois o banco responde e o laço volta ao PollInterval.
        Assert.Equal([2, 4, 8, 1, 1], clock.Delays.Take(5).Select(d => d.TotalSeconds));
        var batchLogs = BatchLogs(logs);
        Assert.Equal([LogLevel.Error, LogLevel.Warning, LogLevel.Warning], batchLogs.Select(l => l.Level));
        Assert.Single(logs.Entries, l => l.Level == LogLevel.Information && l.Message.Contains("recuperado", StringComparison.Ordinal));
    }

    private static List<CapturedLog> BatchLogs(CapturingLoggerProvider logs) =>
        [.. logs.Entries.Where(l => l.Exception is not null)];

    private async Task RunUntilAsync(
        IServiceScopeFactory scopes, OutboxOptions options, RecordingTimeProvider clock, CapturingLoggerProvider logs, Func<bool> done)
    {
        using var loggerFactory = new LoggerFactory([logs]);
        var processor = new OutboxProcessor<IdentityDbContext>(
            scopes,
            factory.Services.GetRequiredService<IIntegrationEventPublisher>(),
            clock,
            Options.Create(options),
            factory.Services.GetRequiredService<OutboxMetrics>(),
            NullLogger<OutboxProcessor<IdentityDbContext>>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OutboxPublisherService<IdentityDbContext>.EnabledKey] = "true" })
            .Build();
        using var service = new OutboxPublisherService<IdentityDbContext>(
            processor, configuration, Options.Create(options), clock, loggerFactory.CreateLogger<OutboxPublisherService<IdentityDbContext>>());

        await service.StartAsync(Ct);
        try
        {
            var deadline = DateTimeOffset.UtcNow + Wait;
            while (!done())
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "O laço não chegou ao número de ciclos esperado.");
                await Task.Delay(10, Ct);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Simula o banco fora: as primeiras <paramref name="failures"/> aberturas de escopo falham.</summary>
    private sealed class FailingScopeFactory(IServiceScopeFactory inner, int failures) : IServiceScopeFactory
    {
        private int _calls;

        public IServiceScope CreateScope() =>
            Interlocked.Increment(ref _calls) <= failures
                ? throw new InvalidOperationException("banco indisponível (simulado)")
                : inner.CreateScope();
    }

    /// <summary>Grava cada espera pedida e a libera imediatamente; o horário vem do relógio de teste.</summary>
    private sealed class RecordingTimeProvider(TimeProvider inner) : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> _delays = new();

        public IReadOnlyList<TimeSpan> Delays => [.. _delays];

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _delays.Enqueue(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }
}
