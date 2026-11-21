namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// O broker recebeu a mensagem e a recusou (nack). Diferente de falha de conexão, canal ou timeout:
/// diz respeito só a esta mensagem, então o lote do outbox segue para as próximas.
/// </summary>
public sealed class IntegrationEventRejectedException : Exception
{
    public IntegrationEventRejectedException()
    {
    }

    public IntegrationEventRejectedException(string message)
        : base(message)
    {
    }

    public IntegrationEventRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
