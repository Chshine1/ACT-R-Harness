using Harness.Abstractions.Actr;

namespace Harness.Core.NeuroCore;

public class BuffersView
{
    private readonly Dictionary<string, object?> _data;

    public BuffersView(IEnumerable<BufferState> bufferStates)
    {
        _data = bufferStates.ToDictionary(
            bufferState => bufferState.ModuleId, object? (bufferState) => ProtobufDataConverter.ToPlainObjectMap(bufferState.Data),
            StringComparer.Ordinal);
    }

    public object? Get(string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        object? current = _data;

        foreach (var part in parts)
        {
            switch (current)
            {
                case IDictionary<string, object?> map:
                    map.TryGetValue(part, out current);
                    break;
                case IReadOnlyDictionary<string, object?> map:
                    map.TryGetValue(part, out current);
                    break;
                case IList<object?> list when int.TryParse(part, out var index) && index >= 0 && index < list.Count:
                    current = list[index];
                    break;
                default:
                    return null;
            }

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        return _data;
    }
}
