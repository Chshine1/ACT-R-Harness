using System.Collections;

namespace Harness.Shared.Observability;

public static class ObservabilityFormatter
{
    public static object? Summarize(object? value, int depth = 0)
    {
        if (value is null)
        {
            return null;
        }

        if (depth >= 3)
        {
            return value.GetType().Name;
        }

        return value switch
        {
            string text => text.Length <= 160 ? text : $"{text[..157]}...",
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            Enum => value.ToString(),
            IDictionary dictionary => SummarizeDictionary(dictionary, depth),
            IEnumerable enumerable and not string => SummarizeEnumerable(enumerable, depth),
            Exception ex => new
            {
                type = ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message
            },
            _ => value.GetType().Name
        };
    }

    private static Dictionary<string, object?> SummarizeDictionary(IDictionary dictionary, int depth)
    {
        var entries = new Dictionary<string, object?>();
        var index = 0;
        foreach (var entry in dictionary.Cast<DictionaryEntry>().TakeWhile(_ => index < 8))
        {
            entries[entry.Key.ToString() ?? "<null>"] = Summarize(entry.Value, depth + 1);
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
