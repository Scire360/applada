using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Applada.Modules.Discovery.Domain;

namespace Applada.Modules.Discovery.Api;

internal static class VenueCursor
{
    private const string Kind = "my-venues";

    public static string Encode(Venue venue)
    {
        var text = FormattableString.Invariant($"{Kind}|{venue.CreatedAt.UtcTicks}|{venue.Id:D}");
        return Base64Url.EncodeToString(Encoding.ASCII.GetBytes(text));
    }

    public static bool TryParse(string? cursor, out (DateTimeOffset CreatedAt, Guid Id)? after)
    {
        after = null;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        if (cursor.Length > 128)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[128];
        if (Base64Url.DecodeFromChars(cursor, buffer, out _, out var written, isFinalBlock: true) != OperationStatus.Done)
        {
            return false;
        }

        var keys = Encoding.ASCII.GetString(buffer[..written]).Split('|');
        if (keys.Length != 3 || keys[0] != Kind
            || !long.TryParse(keys[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks || !Guid.TryParseExact(keys[2], "D", out var id))
        {
            return false;
        }

        after = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
