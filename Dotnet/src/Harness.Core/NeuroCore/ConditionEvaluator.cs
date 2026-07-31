using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core.NeuroCore;

public class ConditionEvaluator(
    SymbolicMatcher symbolicMatcher,
    FuzzyConditionEvaluator fuzzyConditionEvaluator,
    ILogger<ConditionEvaluator> logger)
    : IProvideLogger
{
    public ILogger Logger => logger;

    [ObserveBoundary]
    public async Task<IReadOnlyList<string>> EvaluateAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellationToken = default)
    {
        var view = new BuffersView(bufferStates);
        var satisfiedRuleIds = new List<string>();
        var fuzzyCandidates = new List<ProceduralCondition>();

        foreach (var condition in conditions)
        {
            var symbolicMatch = symbolicMatcher.Evaluate(
                ProtobufDataConverter.ToPlainObjectMap(condition.Condition),
                view);

            if (symbolicMatch)
            {
                satisfiedRuleIds.Add(condition.RuleId);
                continue;
            }

            if (condition.Semantics.Fields.Count > 0)
            {
                fuzzyCandidates.Add(condition);
            }
        }

        if (fuzzyCandidates.Count == 0)
        {
            return satisfiedRuleIds;
        }

        var fuzzyMatches = await fuzzyConditionEvaluator.EvaluateAsync(
            fuzzyCandidates,
            view,
            cancellationToken);

        satisfiedRuleIds.AddRange(fuzzyMatches);
        return satisfiedRuleIds;
    }
}
