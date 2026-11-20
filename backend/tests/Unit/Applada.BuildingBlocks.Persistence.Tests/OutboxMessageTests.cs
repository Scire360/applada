using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence.Outbox;

namespace Applada.BuildingBlocks.Persistence.Tests;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordFailure_keeps_short_error_intact()
    {
        var message = NewMessage();

        message.RecordFailure("falha curta", Now.AddSeconds(2));

        Assert.Equal("falha curta", message.LastError);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(Now.AddSeconds(2), message.NextAttemptAt);
    }

    [Fact]
    public void RecordFailure_truncates_long_error_to_max_length()
    {
        var message = NewMessage();

        message.RecordFailure(new string('x', OutboxMessage.MaxErrorLength + 10), Now);

        Assert.Equal(new string('x', OutboxMessage.MaxErrorLength), message.LastError);
    }

    [Fact]
    public void RecordFailure_does_not_split_surrogate_pair_at_the_cut()
    {
        var message = NewMessage();
        // U+1F3C6 (fora do BMP) ocupa dois chars, um par substituto, nas posições 1999 e 2000: cortar em 2000 o partiria.
        var error = new string('x', OutboxMessage.MaxErrorLength - 1) + "\U0001F3C6" + "resto";

        message.RecordFailure(error, Now);

        Assert.Equal(new string('x', OutboxMessage.MaxErrorLength - 1), message.LastError);
        Assert.False(char.IsHighSurrogate(message.LastError![^1]));
    }

    private static OutboxMessage NewMessage() => OutboxMessage.From(new SampleEvent(Guid.CreateVersion7()), "test", Now);

    private sealed record SampleEvent(Guid Id) : IntegrationEvent
    {
        public override string EventType => "test.sample.v1";

        public override int SchemaVersion => 1;

        public override Guid AggregateId => Id;
    }
}
