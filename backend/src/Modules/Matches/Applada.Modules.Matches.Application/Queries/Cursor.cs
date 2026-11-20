using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Cursor opaco de paginação por chave (keyset): base64url de <c>{"k":"&lt;kind&gt;","v":[...]}</c>. O cursor só
/// posiciona a leitura; quem pode ver o quê continua decidido pela consulta. Qualquer coisa fora do formato — base64
/// inválido, JSON estranho, propriedades a mais, kind de outra listagem, tamanho exagerado — é recusada por
/// <see cref="TryDecode"/>, e o chamador valida tipo e faixa de cada chave.
/// </summary>
public static class Cursor
{
    public const string ParticipantsKind = "participants";
    public const string MyMatchesKind = "my-matches";

    /// <summary>Um cursor legítimo tem menos de 150 caracteres; acima disso nem é decodificado.</summary>
    private const int MaxLength = 256;

    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 4 };

    /// <summary>Chaves aceitas: <see cref="int"/>, <see cref="long"/>, <see cref="Guid"/> e <see cref="DateTimeOffset"/> (ISO 8601).</summary>
    public static string Encode(string kind, params object[] keys)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(keys);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("k", kind);
            writer.WriteStartArray("v");
            foreach (var key in keys)
            {
                switch (key)
                {
                    case int value:
                        writer.WriteNumberValue(value);
                        break;
                    case long value:
                        writer.WriteNumberValue(value);
                        break;
                    case Guid value:
                        writer.WriteStringValue(value);
                        break;
                    case DateTimeOffset value:
                        writer.WriteStringValue(value);
                        break;
                    default:
                        throw new ArgumentException($"Tipo de chave de cursor não suportado: {key?.GetType().Name ?? "null"}.", nameof(keys));
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Decodifica <paramref name="cursor"/> se ele for exatamente um objeto com <c>k</c> igual a
    /// <paramref name="expectedKind"/> e <c>v</c> array. Nunca lança por causa do conteúdo.
    /// </summary>
    public static bool TryDecode(string? cursor, string expectedKind, out JsonElement[] keys)
    {
        keys = [];
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength)
        {
            return false;
        }

        var bytes = new byte[Base64Url.GetMaxDecodedLength(cursor.Length)];
        var status = Base64Url.DecodeFromChars(cursor, bytes, out var consumed, out var written, isFinalBlock: true);
        if (status != OperationStatus.Done || consumed != cursor.Length)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, written), ParseOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            JsonElement? kind = null;
            JsonElement? values = null;
            var count = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                count++;
                if (property.NameEquals("k"))
                {
                    kind = property.Value;
                }
                else if (property.NameEquals("v"))
                {
                    values = property.Value;
                }
                else
                {
                    return false;
                }
            }

            if (count != 2
                || kind is not { ValueKind: JsonValueKind.String } k
                || values is not { ValueKind: JsonValueKind.Array } v
                || !k.ValueEquals(expectedKind))
            {
                return false;
            }

            keys = [.. v.EnumerateArray().Select(e => e.Clone())];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
