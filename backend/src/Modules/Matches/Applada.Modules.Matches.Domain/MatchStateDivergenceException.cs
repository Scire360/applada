namespace Applada.Modules.Matches.Domain;

/// <summary>
/// Os contadores da partida (<see cref="Match.ConfirmedCount"/>, <see cref="Match.WaitlistCount"/>) não batem com as
/// inscrições entregues pelo chamador: bug ou alteração feita fora da aplicação. Nada foi alterado no agregado.
/// Não é erro de regra (não vira 4xx): a operação aborta e o erro sobe, para que a divergência seja investigada.
/// </summary>
public sealed class MatchStateDivergenceException : ArgumentException
{
    public MatchStateDivergenceException()
    {
    }

    public MatchStateDivergenceException(string message)
        : base(message)
    {
    }

    public MatchStateDivergenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MatchStateDivergenceException(string message, string paramName)
        : base(message, paramName)
    {
    }
}
