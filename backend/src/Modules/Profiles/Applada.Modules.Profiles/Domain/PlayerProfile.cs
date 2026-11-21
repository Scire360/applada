namespace Applada.Modules.Profiles.Domain;

internal sealed class PlayerProfile(Guid userId, string displayName)
{
    public Guid UserId { get; private set; } = userId;
    public string DisplayName { get; private set; } = displayName;
}
