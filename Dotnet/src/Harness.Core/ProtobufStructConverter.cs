using Google.Protobuf.WellKnownTypes;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Core;

public static class ProtobufStructConverter
{
    public static Struct ToStruct(IReadOnlyDictionary<string, object?> data)
    {
        var result = new Struct();
        foreach (var field in data)
        {
            result.Fields[field.Key] = ToValue(field.Value);
        }

        return result;
    }

    private static Value ToValue(object? value)
    {
        return value switch
        {
            null => Value.ForNull(),
            Value protobufValue => protobufValue,
            Struct protobufStruct => Value.ForStruct(protobufStruct),
            string text => Value.ForString(text),
            bool flag => Value.ForBool(flag),
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Value.ForNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            JsonNode node => ToValue(JsonNodeToPlainObject(node)),
            IReadOnlyDictionary<string, object?> map => Value.ForStruct(ToStruct(map)),
            IDictionary<string, object?> map => Value.ForStruct(
                ToStruct(new Dictionary<string, object?>(map, StringComparer.Ordinal))),
            IDictionary<string, string> stringMap => Value.ForStruct(ToStruct(
                stringMap.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal))),
            IEnumerable sequence => Value.ForList((from object? item in sequence select ToValue(item)).ToArray()),
            _ => Value.ForString(value.ToString() ?? string.Empty)
        };
    }

    public static object? JsonNodeToPlainObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(node);
        return JsonElementToPlainObject(element);
    }

    private static object? JsonElementToPlainObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => JsonElementToPlainObject(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToPlainObject).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
