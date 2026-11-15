using System.Text.Json;
using System.Text.Json.Serialization;

namespace Applada.Modules.Matches.Api;

/// <summary>Lê uma data e hora ISO 8601 com o fuso explícito, sem recorrer ao fuso local do servidor.</summary>
internal sealed class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private const int IsoDateLength = 10;

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out var value))
        {
            // O parser já validou a data e a hora. Os hífens da data não representam um offset.
            var time = reader.GetString().AsSpan(IsoDateLength);
            if (time.IndexOfAny('Z', '+', '-') >= 0)
            {
                return value;
            }
        }

        throw new JsonException("Informe a data e hora com Z ou offset explícito.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
