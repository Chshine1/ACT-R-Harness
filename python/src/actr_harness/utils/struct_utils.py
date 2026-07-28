from betterproto.lib.google.protobuf import Struct, Value
from betterproto.lib.std.google.protobuf import ListValue


def _to_value(val):
    if isinstance(val, dict):
        return Value(struct_value=dict_to_struct(val))
    elif isinstance(val, list):
        return Value(list_value=ListValue(values=[_to_value(v) for v in val]))
    elif isinstance(val, str):
        return Value(string_value=val)
    elif isinstance(val, bool):
        return Value(bool_value=val)
    elif isinstance(val, (int, float)):
        return Value(number_value=val)
    else:
        raise TypeError(f"Unsupported type for Value: {type(val)}")


def dict_to_struct(data: dict) -> Struct:
    s = Struct()
    for key, value in data.items():
        s.fields[key] = _to_value(value)
    return s
