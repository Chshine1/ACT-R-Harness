using System.Text.Json.Nodes;
using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core.NeuroCore;

public class ActionResolver(LlmClient llmClient, ILogger<ActionResolver> logger) : IProvideLogger
{
    private sealed record SemanticEntry(
        string TargetModuleId,
        string Command,
        Dictionary<string, object?> ExistingParams,
        Dictionary<string, object?> Semantic,
        Dictionary<string, Dictionary<string, object?>> Meta,
        string CommandSchema,
        Dictionary<string, object?> SemanticSources,
        Dictionary<string, string> SemanticParamLeaves);

    public ILogger Logger => logger;

    [TraceSpan]
    public async Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellationToken = default)
    {
        var view = new BuffersView(currentStates);
        var keyedSchemas = schemas.ToDictionary(
            schema => schema.ModuleId,
            schema => schema.CommandSchemas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

        var commandSemantics = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var neuroIntents = new List<Dictionary<string, object?>>();
        var metaInstructions = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);

        foreach (var semanticsEntry in actionIntent.Semantics)
        {
            var parts = semanticsEntry.Key.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var semantics = ProtobufDataConverter.ToPlainObjectMap(semanticsEntry.Value);
            switch (parts[0])
            {
                case "meta":
                    metaInstructions[parts[1]] = semantics;
                    break;
                case "neuro":
                    neuroIntents.Add(semantics);
                    break;
                case "command":
                    commandSemantics[parts[1]] = semantics;
                    break;
            }
        }

        var determinedOperations = new List<BufferOperation>();
        var semanticEntries = new List<SemanticEntry>();

        foreach (var (alias, baseOperation) in actionIntent.Commands)
        {
            var baseParams = ResolvePlaceholderParams(
                ProtobufDataConverter.ToPlainObjectMap(baseOperation.Params),
                view);
            var semantic = commandSemantics.GetValueOrDefault(alias);

            if (semantic is not null || metaInstructions.Count > 0)
            {
                if (!keyedSchemas.TryGetValue(baseOperation.TargetModuleId, out var moduleSchemas)
                    || !moduleSchemas.TryGetValue(baseOperation.Command, out var schema))
                {
                    throw new InvalidOperationException(
                        $"Missing schema for target_module_id='{baseOperation.TargetModuleId}', command='{baseOperation.Command}', alias='{alias}'.");
                }

                var sources = new Dictionary<string, object?>(StringComparer.Ordinal);
                var semanticParamLeaves = new Dictionary<string, string>(StringComparer.Ordinal);
                if (semantic is not null)
                {
                    foreach (var source in ReadSources(semantic))
                    {
                        sources[source] = view.Get(source);
                    }

                    if (semantic.TryGetValue("params", out var paramsValue))
                    {
                        semanticParamLeaves = FlattenSemanticParams(paramsValue);
                    }
                }

                semanticEntries.Add(new SemanticEntry(
                    baseOperation.TargetModuleId,
                    baseOperation.Command,
                    baseParams,
                    semantic ?? new Dictionary<string, object?>(StringComparer.Ordinal),
                    metaInstructions,
                    schema,
                    sources,
                    semanticParamLeaves));
            }
            else
            {
                determinedOperations.Add(new BufferOperation
                {
                    TargetModuleId = baseOperation.TargetModuleId,
                    Command = baseOperation.Command,
                    Params = ProtobufStructConverter.ToStruct(baseParams)
                });
            }
        }

        if (semanticEntries.Count > 0)
        {
            var semanticOperations = await ResolveSemanticCommandsAsync(
                determinedOperations,
                semanticEntries,
                keyedSchemas,
                cancellationToken);
            determinedOperations.AddRange(semanticOperations);
        }

        if (neuroIntents.Count <= 0) return determinedOperations;

        var neuroOperations = await DecodeNeuroIntentsAsync(
            determinedOperations,
            neuroIntents,
            currentStates,
            keyedSchemas,
            cancellationToken);
        determinedOperations.AddRange(neuroOperations);

        return determinedOperations;
    }

    [TraceSpan]
    private async Task<IReadOnlyList<BufferOperation>> ResolveSemanticCommandsAsync(
        IReadOnlyList<BufferOperation> determinedOperations,
        IReadOnlyList<SemanticEntry> semanticEntries,
        IReadOnlyDictionary<string, Dictionary<string, string>> keyedSchemas,
        CancellationToken cancellationToken)
    {
        var promptData = new Dictionary<string, object?>
        {
            ["determined_ops"] = determinedOperations.Select(OperationSummary).ToList(),
            ["semantic_commands"] = semanticEntries.Select(entry => new Dictionary<string, object?>
            {
                ["target_module_id"] = entry.TargetModuleId,
                ["command"] = entry.Command,
                ["existing_params"] = entry.ExistingParams,
                ["semantic"] = entry.Semantic,
                ["meta"] = entry.Meta,
                ["command_schema"] = entry.CommandSchema,
                ["semantic_sources"] = entry.SemanticSources,
                ["semantic_param_leaves"] = entry.SemanticParamLeaves
            }).ToList(),
            ["module_schemas"] = keyedSchemas
        };

        const string systemPrompt =
            "You are given already determined operations (do NOT include them in your output) "
            + "and a set of incomplete semantic commands with parameters described in natural language. "
            + "For each semantic command, resolve it into zero or more concrete operations according to "
            + "its semantic description and any meta policies (e.g., skip if required sources are missing). "
            + "Return ONLY the operations derived from the semantic commands (the already determined ones "
            + "will be kept automatically). "
            + "Output a strict JSON array of objects with keys: target_module_id, command, params. "
            + "No extra text.";

        var response = await llmClient.ChatJsonAsync(promptData, systemPrompt, cancellationToken);
        return ParseOperations(response);
    }

    [TraceSpan]
    private async Task<IReadOnlyList<BufferOperation>> DecodeNeuroIntentsAsync(
        IReadOnlyList<BufferOperation> determinedOperations,
        IReadOnlyList<Dictionary<string, object?>> neuroIntents,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyDictionary<string, Dictionary<string, string>> keyedSchemas,
        CancellationToken cancellationToken)
    {
        var promptData = new Dictionary<string, object?>
        {
            ["buffers"] = currentStates.Select(state => new Dictionary<string, object?>
            {
                ["module_id"] = state.ModuleId,
                ["data"] = ProtobufDataConverter.ToPlainObjectMap(state.Data)
            }).ToList(),
            ["module_schemas"] = keyedSchemas,
            ["partial_commands"] = determinedOperations.Select(operation => new Dictionary<string, object?>
            {
                ["target_module_id"] = operation.TargetModuleId,
                ["command"] = operation.Command,
                ["existing_params"] = ProtobufDataConverter.ToPlainObjectMap(operation.Params)
            }).ToList(),
            ["neural_intents"] = neuroIntents
        };

        const string systemPrompt =
            "Translate partial commands and neural intents into concrete operations. "
            + "Each operation must use a valid module_id from schemas, a command defined there, "
            + "and parameters with correct types. "
            + "Output a strict JSON array of objects with keys: target_module_id, command, params. "
            + "No commentary.";

        var response = await llmClient.ChatJsonAsync(promptData, systemPrompt, cancellationToken);
        return ParseOperations(response);
    }

    private static Dictionary<string, object?> ResolvePlaceholderParams(
        IReadOnlyDictionary<string, object?> value,
        BuffersView view)
    {
        return value.ToDictionary(pair => pair.Key, pair => ResolvePlaceholderValue(pair.Value, view),
            StringComparer.Ordinal);
    }

    private static object? ResolvePlaceholderValue(object? value, BuffersView view)
    {
        return value switch
        {
            string text when text.StartsWith("${", StringComparison.Ordinal) &&
                             text.EndsWith("}", StringComparison.Ordinal) => view.Get(text[2..^1]) ?? text,
            IReadOnlyDictionary<string, object?> map => map.ToDictionary(pair => pair.Key,
                pair => ResolvePlaceholderValue(pair.Value, view), StringComparer.Ordinal),
            IDictionary<string, object?> map => map.ToDictionary(pair => pair.Key,
                pair => ResolvePlaceholderValue(pair.Value, view), StringComparer.Ordinal),
            IEnumerable<object?> list => list.Select(item => ResolvePlaceholderValue(item, view)).ToList(),
            _ => value
        };
    }

    private static Dictionary<string, string> FlattenSemanticParams(object? value, string prefix = "")
    {
        while (true)
        {
            switch (value)
            {
                case IReadOnlyDictionary<string, object?> map:
                {
                    var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var entry in map)
                    {
                        var path = string.IsNullOrEmpty(prefix) ? entry.Key : $"{prefix}.{entry.Key}";
                        foreach (var leaf in FlattenSemanticParams(entry.Value, path))
                        {
                            leaves[leaf.Key] = leaf.Value;
                        }
                    }

                    return leaves;
                }
                case IDictionary<string, object?> map:
                    value = new Dictionary<string, object?>(map, StringComparer.Ordinal);
                    continue;
                case string leaf:
                    return new Dictionary<string, string>(StringComparer.Ordinal) { [prefix] = leaf };
                default:
                    throw new InvalidOperationException(
                        $"Semantic param leaf at '{(string.IsNullOrEmpty(prefix) ? "<root>" : prefix)}' must be a string, got {value?.GetType().Name ?? "null"}.");
            }
        }
    }

    private static IEnumerable<string> ReadSources(Dictionary<string, object?> semantic)
    {
        if (!semantic.TryGetValue("sources", out var sourcesValue))
        {
            return [];
        }

        return sourcesValue switch
        {
            IEnumerable<object?> list => list.OfType<string>(),
            string single => [single],
            _ => []
        };
    }

    private static IReadOnlyList<BufferOperation> ParseOperations(JsonNode? response)
    {
        if (response is not JsonArray array)
        {
            return [];
        }

        var operations = new List<BufferOperation>();
        foreach (var item in array)
        {
            if (item is null ||
                ProtobufStructConverter.JsonNodeToPlainObject(item) is not Dictionary<string, object?> operationMap)
            {
                continue;
            }

            if (!operationMap.TryGetValue("target_module_id", out var targetModuleIdValue)
                || targetModuleIdValue is not string targetModuleId
                || !operationMap.TryGetValue("command", out var commandValue)
                || commandValue is not string command)
            {
                continue;
            }

            var plainParams = operationMap.TryGetValue("params", out var paramsValue)
                              && paramsValue is Dictionary<string, object?> paramsMap
                ? paramsMap
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            operations.Add(new BufferOperation
            {
                TargetModuleId = targetModuleId,
                Command = command,
                Params = ProtobufStructConverter.ToStruct(plainParams)
            });
        }

        return operations;
    }

    private static Dictionary<string, object?> OperationSummary(BufferOperation operation)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["target_module_id"] = operation.TargetModuleId,
            ["command"] = operation.Command,
            ["params"] = ProtobufDataConverter.ToPlainObjectMap(operation.Params)
        };
    }
}