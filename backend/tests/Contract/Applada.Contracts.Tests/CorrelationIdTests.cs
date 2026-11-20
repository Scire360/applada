using System.Diagnostics;
using Applada.BuildingBlocks.Contracts;

namespace Applada.Contracts.Tests;

/// <summary>
/// Regra única do <c>correlationId</c> gravado no outbox por todos os módulos: o trace W3C corrente; sem trace, o
/// fallback de quem chama (na API, o id da requisição); sem os dois, um id novo.
/// </summary>
public class CorrelationIdTests
{
    [Fact]
    public void Uses_trace_id_of_current_activity_in_hex()
    {
        Activity.Current = null;
        using var activity = new Activity("teste").SetIdFormat(ActivityIdFormat.W3C).Start();

        var correlationId = CorrelationId.Current(fallback: "req-1");

        Assert.Equal(activity.TraceId.ToHexString(), correlationId);
        Assert.Matches("^[0-9a-f]{32}$", correlationId);
    }

    [Fact]
    public void Activity_without_w3c_trace_falls_back_instead_of_zero_trace()
    {
        Activity.Current = null;
        // Formato hierárquico: TraceId fica default, cujo texto seria "000…0" (mesmo valor para toda requisição).
        using var activity = new Activity("teste").SetIdFormat(ActivityIdFormat.Hierarchical).Start();
        Assert.Equal(default, activity.TraceId);

        Assert.Equal("req-1", CorrelationId.Current(fallback: "req-1"));
    }

    [Fact]
    public void Without_activity_uses_fallback()
    {
        Activity.Current = null;

        Assert.Equal("req-1", CorrelationId.Current(fallback: "req-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_activity_or_fallback_generates_a_new_id(string? fallback)
    {
        Activity.Current = null;

        var first = CorrelationId.Current(fallback);
        var second = CorrelationId.Current(fallback);

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }
}
