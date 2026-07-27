using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Harness.Core;

public static class ProtobufDataConverter
{
    public static Dictionary<string, object?> ToPlainObjectMap(Struct data)
    {
        return data.Fields.ToDictionary(field => field.Key, field => ToPlainObject(field.Value));
    }

    public static Dictionary<string, object?> ToPlainObjectMap(MapField<string, Value> fields)
    {
        return fields.ToDictionary(field => field.Key, field => ToPlainObject(field.Value));
    }

    private static object? ToPlainObject(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.NullValue => null,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StructValue => ToPlainObjectMap(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values.Select(ToPlainObject).ToList(),
            _ => null
        };
    }
}
