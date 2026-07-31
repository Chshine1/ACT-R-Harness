using System.Text.Json.Nodes;
using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core.NeuroCore;

public class FuzzyConditionEvaluator(LlmClient llm, ILogger<FuzzyConditionEvaluator> logger) : IProvideLogger
{
    public ILogger Logger => logger;

    [ObserveBoundary]
    public async Task<IReadOnlyList<string>> EvaluateAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        BuffersView view,
        CancellationToken cancellationToken = default)
    {
        var prompt = new Dictionary<string, object?>
        {
            ["buffers"] = view.ToDictionary(),
            ["conditions"] = conditions.Select(condition => new Dictionary<string, object?>
            {
                ["rule_id"] = condition.RuleId,
                ["symbolic"] = ProtobufDataConverter.ToPlainObjectMap(condition.Condition),
                ["semantic_hint"] = ProtobufDataConverter.ToPlainObjectMap(condition.Semantics)
            }).ToList()
        };

        const string systemPrompt =
            "You are given buffers (world state) and conditions with optional semantic hints. "
            + "Determine which conditions are satisfied. "
            + "Return ONLY a JSON array of the satisfied rule_id strings. "
            + "No extra text, no explanation.";

        var result = await llm.ChatJsonAsync(prompt, systemPrompt, cancellationToken);
        if (result is not JsonArray array)
        {
            logger.LogWarning("Fuzzy condition evaluator returned a non-list payload.");
            return [];
        }

        return array
            .Select(node => node?.GetValue<string>())
            .Where(ruleId => !string.IsNullOrWhiteSpace(ruleId))
            .Cast<string>()
            .ToList();
    }
}
