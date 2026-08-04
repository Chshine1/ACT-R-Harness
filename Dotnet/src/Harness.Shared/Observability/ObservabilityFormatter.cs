using System.Collections;
using System.Text.Json;

namespace Harness.Shared.Observability;

public static class ObservabilityFormatter
{
    public static object? Summarize(object? value, int depth = 0)
    {
        if (value is null) return null;
        if (depth >= 3) return value.GetType().Name;

        if (value is JsonElement je)
            return SummarizeJsonElement(je, depth);

        return value switch
        {
            string text => text.Length <= 160 ? text : $"{text[..157]}...",
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double
                or decimal => value,
            Enum => value.ToString(),
            IDictionary dictionary => SummarizeDictionary(dictionary, depth),
            IEnumerable enumerable and not string => SummarizeEnumerable(enumerable, depth),
            Exception ex => new { type = ex.GetType().FullName ?? ex.GetType().Name, ex.Message },
            _ => value.GetType().Name
        };
    }

    private static object? SummarizeJsonElement(JsonElement element, int depth) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => Summarize(element.GetString(), depth + 1),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.Array => SummarizeJsonArray(element, depth),
        JsonValueKind.Object => SummarizeJsonObject(element, depth),
        _ => element.GetRawText()
    };

    private static Dictionary<string, object?> SummarizeJsonObject(JsonElement element, int depth)
    {
        var dict = new Dictionary<string, object?>();
        var count = 0;
        foreach (var prop in element.EnumerateObject().TakeWhile(_ => count++ < 8))
        {
            dict[prop.Name] = SummarizeJsonElement(prop.Value, depth + 1);
        }

        if (element.GetPropertyCount() > count)
            dict["_truncated"] = element.GetPropertyCount() - count;
        return dict;
    }

    private static Dictionary<string, object?> SummarizeJsonArray(JsonElement element, int depth)
    {
        var total = element.GetArrayLength();
        var previewCount = 0;
        var items = element.EnumerateArray().TakeWhile(_ => previewCount++ < 6)
            .Select(item => SummarizeJsonElement(item, depth + 1)).ToList();
        return new Dictionary<string, object?>
        {
            ["count"] = total,
            ["preview"] = items
        };
    }

    private static Dictionary<string, object?> SummarizeDictionary(IDictionary dictionary, int depth)
    {
        var entries = new Dictionary<string, object?>();
        var index = 0;

        foreach (var key in dictionary.Keys)
        {
            if (index >= 8) break;
            if (key is null)
            {
                continue;
            }

            var keyString = key.ToString() ?? "<null>";
            object? value;
            try
            {
                value = dictionary[key];
            }
            catch
            {
                value = "<error>";
            }

            entries[keyString] = Summarize(value, depth + 1);
            index++;
        }

        if (dictionary.Count > index)
        {
            entries["_truncated"] = dictionary.Count - index;
        }

        return entries;
    }

    private static Dictionary<string, object?> SummarizeEnumerable(IEnumerable enumerable, int depth)
    {
        var items = new List<object?>();
        var total = 0;
        foreach (var item in enumerable)
        {
            total++;
            if (items.Count < 6)
            {
                items.Add(Summarize(item, depth + 1));
            }
        }

        return new Dictionary<string, object?>
        {
            ["count"] = total,
            ["preview"] = items
        };
    }
}