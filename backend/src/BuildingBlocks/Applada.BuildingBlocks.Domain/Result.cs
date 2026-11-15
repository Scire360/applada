namespace Applada.BuildingBlocks.Domain;

/// <summary>
/// Resultado de uma operação sem valor de retorno: sucesso ou um <see cref="Domain.Error"/> de regra.
/// <c>default(Result)</c> equivale a <see cref="Success"/>.
/// </summary>
public readonly struct Result
{
    private readonly Error? _error;

    private Result(Error error) => _error = error;

    public bool IsSuccess => _error is null;

    /// <summary>O erro da falha. Ler em caso de sucesso é erro de programação.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("Resultado de sucesso não tem erro.");

    public static Result Success() => default;

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Resultado de uma operação que devolve <typeparamref name="T"/> em caso de sucesso.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error) => _error = error;

    public bool IsSuccess { get; }

    /// <summary>O valor do sucesso. Ler em caso de falha é erro de programação.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Resultado de falha não tem valor.");

    /// <summary>O erro da falha. Ler em caso de sucesso é erro de programação.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("Resultado de sucesso não tem erro.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }
}
