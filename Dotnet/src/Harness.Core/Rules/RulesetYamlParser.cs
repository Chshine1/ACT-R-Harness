using System.Globalization;
using System.Text.Json;

namespace Harness.Core.Rules;

public static class RulesetYamlParser
{
    public static object? ParseFile(string path)
    {
        using var reader = File.OpenText(path);
        var lines = new List<YamlLine>();
        var lineNumber = 0;

        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var trimmedStart = rawLine.TrimStart();
            if (trimmedStart.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(new YamlLine(
                rawLine.Length - trimmedStart.Length,
                trimmedStart,
                lineNumber));
        }

        if (lines.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var parser = new Parser(lines);
        return parser.ParseDocument();
    }

    private sealed record YamlLine(int Indent, string Content, int Number);

    private sealed class Parser(IReadOnlyList<YamlLine> lines)
    {
        private int _index;

        public object? ParseDocument()
        {
            return ParseBlock(lines[_index].Indent);
        }

        private object? ParseBlock(int indent)
        {
            if (_index >= lines.Count)
            {
                return null;
            }

            return IsSequenceLine(lines[_index], indent)
                ? ParseSequence(indent)
                : ParseMapping(indent);
        }

        private Dictionary<string, object?> ParseMapping(int indent)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);

            while (_index < lines.Count)
            {
                var line = lines[_index];
                if (line.Indent < indent)
                {
                    break;
                }

                if (line.Indent > indent)
                {
                    throw new InvalidOperationException(
                        $"Unexpected indentation on line {line.Number}: '{line.Content}'.");
                }

                if (line.Content.StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                var separatorIndex = line.Content.IndexOf(':');
                if (separatorIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Expected mapping entry on line {line.Number}: '{line.Content}'.");
                }

                var key = line.Content[..separatorIndex].Trim();
                var rawValue = line.Content[(separatorIndex + 1)..].Trim();
                _index++;
                map[key] = ParseValue(rawValue, line.Indent);
            }

            return map;
        }

        private List<object?> ParseSequence(int indent)
        {
            var items = new List<object?>();

            while (_index < lines.Count)
            {
                var line = lines[_index];
                if (line.Indent < indent)
                {
                    break;
                }

                if (!IsSequenceLine(line, indent))
                {
                    break;
                }

                var remainder = line.Content[2..].Trim();
                _index++;

                if (string.IsNullOrEmpty(remainder))
                {
                    items.Add(ParseNestedBlockOrEmpty(line.Indent));
                    continue;
                }

                var separatorIndex = remainder.IndexOf(':');
                if (separatorIndex >= 0)
                {
                    var key = remainder[..separatorIndex].Trim();
                    var rawValue = remainder[(separatorIndex + 1)..].Trim();
                    var item = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [key] = ParseValue(rawValue, line.Indent)
                    };

                    if (_index < lines.Count && lines[_index].Indent > line.Indent)
                    {
                        foreach (var nestedEntry in ParseMapping(lines[_index].Indent))
                        {
                            item[nestedEntry.Key] = nestedEntry.Value;
                        }
                    }

                    items.Add(item);
                    continue;
                }

                items.Add(ParseScalar(remainder));
            }

            return items;
        }

        private object? ParseValue(string rawValue, int currentIndent)
        {
            if (rawValue == ">")
            {
                return ParseFoldedScalar(currentIndent);
            }

            if (string.IsNullOrEmpty(rawValue))
            {
                return ParseNestedBlockOrEmpty(currentIndent);
            }

            return ParseScalar(rawValue);
        }

        private object? ParseNestedBlockOrEmpty(int currentIndent)
        {
            if (_index >= lines.Count || lines[_index].Indent <= currentIndent)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            return ParseBlock(lines[_index].Indent);
        }

        private string ParseFoldedScalar(int currentIndent)
        {
            if (_index >= lines.Count || lines[_index].Indent <= currentIndent)
            {
                return string.Empty;
            }

            var childIndent = lines[_index].Indent;
            var parts = new List<string>();
            while (_index < lines.Count)
            {
                var line = lines[_index];
                if (line.Indent < childIndent)
                {
                    break;
                }

                parts.Add(line.Content.Trim());
                _index++;
            }

            return string.Join(' ', parts.Where(part => part.Length > 0));
        }

        private static object? ParseScalar(string rawValue)
        {
            if (rawValue == "{}")
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            if (rawValue == "[]")
            {
                return new List<object?>();
            }

            if (rawValue is "null" or "~")
            {
                return null;
            }

            if (bool.TryParse(rawValue, out var boolean))
            {
                return boolean;
            }

            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            if ((rawValue.StartsWith('"') && rawValue.EndsWith('"'))
                || (rawValue.StartsWith('\'') && rawValue.EndsWith('\'')))
            {
                if (rawValue.StartsWith('"'))
                {
                    return JsonSerializer.Deserialize<string>(rawValue);
                }

                return rawValue[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            return rawValue;
        }

        private static bool IsSequenceLine(YamlLine line, int indent)
        {
            return line.Indent == indent && line.Content.StartsWith("- ", StringComparison.Ordinal);
        }
    }
}
