using Harness.Abstractions.Actr;
using Harness.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Core.Rules;

public sealed record LoadedRule(
    string Id,
    ProceduralCondition Condition,
    NeuroAction Action,
    double Utility);

public class RulesLoader(
    IOptions<ProceduralMemoryOptions> options,
    ILogger<RulesLoader> logger)
{
    private readonly ProceduralMemoryOptions _options = options.Value;

    public IReadOnlyDictionary<string, LoadedRule> LoadRules()
    {
        var path = ResolveRulesPath(_options.RulesPath);
        var root = RulesetYamlParser.ParseFile(path);
        if (root is not Dictionary<string, object?> payload)
        {
            throw new InvalidOperationException($"Ruleset '{path}' did not contain a top-level object.");
        }

        if (!payload.TryGetValue("rules", out var rulesValue) || rulesValue is not List<object?> rawRules)
        {
            throw new InvalidOperationException($"Ruleset '{path}' did not contain a 'rules' array.");
        }

        var loadedRules = new Dictionary<string, LoadedRule>(StringComparer.Ordinal);
        foreach (var rawRule in rawRules.OfType<Dictionary<string, object?>>())
        {
            var ruleId = RequireString(rawRule, "id");
            var rawCondition = OptionalMap(rawRule, "condition");
            var rawAction = OptionalMap(rawRule, "action");

            var condition = new ProceduralCondition
            {
                RuleId = ruleId,
                Condition = ProtobufStructConverter.ToStruct(OptionalMap(rawCondition, "symbolic")),
                Semantics = ProtobufStructConverter.ToStruct(OptionalMap(rawCondition, "semantic"))
            };

            var action = new NeuroAction
            {
                RuleId = ruleId
            };

            foreach (var commandEntry in OptionalMap(rawAction, "commands"))
            {
                if (commandEntry.Value is not Dictionary<string, object?> commandDefinition)
                {
                    continue;
                }

                action.Commands[commandEntry.Key] = new BufferOperation
                {
                    TargetModuleId = RequireString(commandDefinition, "target_module_id"),
                    Command = RequireString(commandDefinition, "command"),
                    Params = ProtobufStructConverter.ToStruct(OptionalMap(commandDefinition, "params"))
                };
            }

            foreach (var semanticsEntry in OptionalMap(rawAction, "semantics"))
            {
                if (semanticsEntry.Value is Dictionary<string, object?> semanticsDefinition)
                {
                    action.Semantics[semanticsEntry.Key] = ProtobufStructConverter.ToStruct(semanticsDefinition);
                }
            }

            loadedRules[ruleId] = new LoadedRule(
                ruleId,
                condition,
                action,
                rawRule.TryGetValue("utility", out var utilityValue) ? Convert.ToDouble(utilityValue) : _options.DefaultUtility);
        }

        logger.LogInformation("Loaded {RuleCount} rules from {Path}.", loadedRules.Count, path);
        return loadedRules;
    }

    private static string ResolveRulesPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "shared", "ruleset", "lab.yml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        throw new FileNotFoundException(
            "Could not locate shared/ruleset/lab.yml. Configure RULESET_PATH to the ruleset file.");
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new InvalidOperationException($"Expected '{key}' to be a non-empty string.");
    }

    private static Dictionary<string, object?> OptionalMap(IReadOnlyDictionary<string, object?> data, string key)
    {
        return data.TryGetValue(key, out var value) && value is Dictionary<string, object?> map
            ? map
            : new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
