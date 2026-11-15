namespace Applada.Modules.Matches.Application.Ports;

/// <summary>Resposta guardada para uma <c>Idempotency-Key</c> de um usuário.</summary>
public sealed record IdempotencyEntry(Guid UserId, string Key, string RequestHash, int StatusCode, string ResponseBody);
