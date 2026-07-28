from betterproto.lib.google.protobuf import Struct, Value


def dict_to_struct(data: dict) -> Struct:
    s = Struct()
    for key, value in data.items():
        if isinstance(value, dict):
            s.fields[key] = Value(struct_value=dict_to_struct(value))
        elif isinstance(value, str):
            s.fields[key] = Value(string_value=value)
        elif isinstance(value, (int, float)):
            s.fields[key] = Value(number_value=value)
        elif isinstance(value, bool):
            s.fields[key] = Value(bool_value=value)
        else:
            raise TypeError(f"Unsupported type for Struct: {type(value)}")
    return s
