using System.Buffers.Text;
using System.Text;

namespace Applada.Api.IntegrationTests.Infrastructure;

internal static class CursorSamples
{
    public static string WithInvalidUtf8Key(string kind)
    {
        // O byte bruto 0xff cobre UTF-8 inválido. A Theory em CursorTests cobre escapes Unicode.
        var bytes = Encoding.UTF8.GetBytes($$"""{"k":"{{kind}}","v":["?"]}""");
        bytes[Array.IndexOf(bytes, (byte)'?')] = 0xff;
        return Base64Url.EncodeToString(bytes);
    }
}
