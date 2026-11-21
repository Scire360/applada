using System.Text;
using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Discovery.Domain;

internal sealed class Venue
{
    internal const int MinNameLength = 3;
    internal const int MaxNameLength = 80;
    internal const int MaxAddressLength = 200;

    private Venue() { }

    public Guid Id { get; private init; }
    public string Name { get; private init; } = null!;
    public string? AddressText { get; private init; }
    public double Latitude { get; private init; }
    public double Longitude { get; private init; }
    public Guid CreatedBy { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    public static Result<Venue> Create(
        string? name, string? addressText, double? latitude, double? longitude, Guid createdBy, DateTimeOffset now)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.EnumerateRunes().Count() is < MinNameLength or > MaxNameLength || name?.Any(char.IsControl) == true)
        {
            return Invalid("name", $"Informe um nome de {MinNameLength} a {MaxNameLength} caracteres, sem caracteres de controle.");
        }

        var trimmedAddress = addressText?.Trim();
        if (trimmedAddress?.EnumerateRunes().Count() > MaxAddressLength || addressText?.Any(char.IsControl) == true)
        {
            return Invalid("addressText", $"Informe um endereço de até {MaxAddressLength} caracteres, sem caracteres de controle.");
        }

        if (latitude is not (>= -90 and <= 90))
        {
            return Invalid("latitude", "Informe uma latitude entre -90 e 90.");
        }

        if (longitude is not (>= -180 and <= 180))
        {
            return Invalid("longitude", "Informe uma longitude entre -180 e 180.");
        }

        return new Venue
        {
            Id = Guid.CreateVersion7(now),
            Name = trimmedName,
            AddressText = string.IsNullOrEmpty(trimmedAddress) ? null : trimmedAddress,
            Latitude = RoundCoordinate(latitude.Value),
            Longitude = RoundCoordinate(longitude.Value),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    private static double RoundCoordinate(double value) =>
        (double)Math.Round((decimal)value, 5, MidpointRounding.AwayFromZero);

    private static Error Invalid(string field, string message) =>
        new("validation", ErrorKind.Validation, "Dados inválidos.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
