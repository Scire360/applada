using System.Diagnostics;

namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// <c>correlationId</c> gravado no outbox (e propagado no envelope), com a mesma regra em todos os módulos.
/// Não depende de HTTP: a camada que conhece a requisição passa o próprio id como <c>fallback</c>.
/// </summary>
public static class CorrelationId
{
    /// <summary>
    /// O trace W3C corrente em hex (32 caracteres) quando há <see cref="Activity.Current"/> com <c>TraceId</c> válido;
    /// senão <paramref name="fallback"/> (na API, <c>HttpContext.TraceIdentifier</c>); sem os dois, um id novo.
    /// </summary>
    /// <remarks>
    /// <c>TraceId</c> default (atividade de formato hierárquico) vira "000…0", igual para toda requisição: é tratado
    /// como ausência de trace.
    /// </remarks>
    public static string Current(string? fallback = null)
    {
        if (Activity.Current is { } activity && activity.TraceId != default)
        {
            return activity.TraceId.ToHexString();
        }

        return string.IsNullOrWhiteSpace(fallback) ? Guid.NewGuid().ToString("N") : fallback;
    }
}
