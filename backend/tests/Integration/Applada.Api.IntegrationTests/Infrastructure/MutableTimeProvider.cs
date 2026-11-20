namespace Applada.Api.IntegrationTests.Infrastructure;

public sealed class MutableTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private TimeSpan _stepPerRead;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            var now = _now;
            _now += _stepPerRead;
            return now;
        }
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_gate)
        {
            _now = value;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _now += delta;
        }
    }

    /// <summary>
    /// Enquanto o retorno não for descartado, cada leitura avança o relógio em <paramref name="step"/>: leituras
    /// sucessivas (inclusive de requisições diferentes) ficam estritamente crescentes. Serve para ordenar no banco
    /// operações que leem o relógio sob o lock da partida.
    /// </summary>
    public IDisposable AdvanceOnEachRead(TimeSpan step)
    {
        lock (_gate)
        {
            _stepPerRead = step;
        }

        return new StopAdvancing(this);
    }

    private sealed class StopAdvancing(MutableTimeProvider clock) : IDisposable
    {
        public void Dispose()
        {
            lock (clock._gate)
            {
                clock._stepPerRead = TimeSpan.Zero;
            }
        }
    }
}
