using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// Formato JSON dos eventos de integração e do envelope: camelCase, enums como string camelCase,
/// sem indentação. Instância somente leitura, compartilhada por quem publica e por quem consome.
/// </summary>
public static class IntegrationEventJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IgnoreEnvelopeMetadata } },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// O System.Text.Json não herda o <see cref="JsonIgnoreAttribute"/> da base para os <c>override</c>:
    /// sem isto, <c>eventType</c>, <c>schemaVersion</c> e <c>aggregateId</c> vazariam para o payload de todo
    /// evento. Remove qualquer propriedade que sobrescreva um metadado declarado em <see cref="IntegrationEvent"/>.
    /// </summary>
    private static void IgnoreEnvelopeMetadata(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IntegrationEvent).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider is PropertyInfo property
                && property.GetMethod?.GetBaseDefinition().DeclaringType == typeof(IntegrationEvent))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
