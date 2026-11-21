namespace Applada.Modules.Discovery.Contracts;

public interface IVenueDirectory
{
    Task<bool> ExistsAsync(Guid venueId, CancellationToken ct);
}
