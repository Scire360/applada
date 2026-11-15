namespace Applada.Modules.Matches.Domain;

/// <summary>Resultado de <see cref="Match.Register"/>. <see cref="Changed"/> é falso quando a inscrição já estava ativa.</summary>
public sealed record RegistrationOutcome(Registration Registration, bool Changed);
