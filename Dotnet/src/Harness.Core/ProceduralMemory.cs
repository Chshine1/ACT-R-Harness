using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Core.Configuration;
using Harness.Core.Rules;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Core;

public class ProceduralMemory : IProceduralMemory, IProvideLogger
{
    private sealed class RuleState
    {
        public required string Id { get; init; }
        public required ProceduralCondition Condition { get; init; }
        public required NeuroAction Action { get; init; }
        public required double Utility { get; set; }
    }

    private readonly Dictionary<string, RuleState> _rules;
    private readonly ILogger<ProceduralMemory> _logger;
    private readonly double _temperature;
    private readonly double _learningRate;
    private readonly Random _random;
    private string? _lastRuleId;

    public ProceduralMemory(
        RulesLoader rulesLoader,
        IOptions<ProceduralMemoryOptions> options,
        IClock clock,
        ILogger<ProceduralMemory> logger)
    {
        _logger = logger;
        _temperature = options.Value.Temperature;
        _learningRate = options.Value.LearningRate;
        _random = new Random(options.Value.RandomSeed);
        _rules = rulesLoader.LoadRules().ToDictionary(
            pair => pair.Key,
            pair => new RuleState
            {
                Id = pair.Value.Id,
                Condition = pair.Value.Condition.Clone(),
                Action = pair.Value.Action.Clone(),
                Utility = pair.Value.Utility
            },
            StringComparer.Ordinal);

        clock.OnTickAsync += (reward, ct) =>
        {
            if (!reward.Training)
            {
                return Task.CompletedTask;
            }

            return _lastRuleId == null ? throw new InvalidOperationException() : LearnUtilityAsync(reward.Reward, ct);
        };
    }

    public ILogger Logger => _logger;

    [TraceSpan]
    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        return _rules.Values
            .Select(rule => rule.Condition.Clone())
            .ToList();
    }

    [TraceSpan]
    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        var applicableRules = _rules.Values
            .Where(rule => satisfiedRuleIds.Contains(rule.Id, StringComparer.Ordinal))
            .ToList();

        if (applicableRules.Count == 0)
        {
            throw new InvalidOperationException(
                $"No applicable rule found for satisfied_rule_ids=[{string.Join(", ", satisfiedRuleIds)}].");
        }

        RuleState selectedRule;
        if (_temperature <= 0)
        {
            selectedRule = applicableRules
                .OrderByDescending(rule => rule.Utility)
                .ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .First();

            TracingModel.AddEvent(
                TracingModel.Events.RuleSelected,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleId,
                        selectedRule.Id),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleCandidateCount,
                        applicableRules.Count),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleSelectionMode,
                        "deterministic")
                });
        }
        else
        {
            var maxUtility = applicableRules.Max(rule => rule.Utility);
            var weightedRules = applicableRules
                .Select(rule => new
                {
                    Rule = rule,
                    Weight = Math.Exp((rule.Utility - maxUtility) / _temperature)
                })
                .ToList();

            var weightSum = weightedRules.Sum(item => item.Weight);
            var sample = _random.NextDouble() * weightSum;
            var cumulative = 0.0;
            selectedRule = weightedRules[^1].Rule;

            foreach (var weightedRule in weightedRules)
            {
                cumulative += weightedRule.Weight;
                if (sample <= cumulative)
                {
                    selectedRule = weightedRule.Rule;
                    break;
                }
            }

            TracingModel.AddEvent(
                TracingModel.Events.RuleSelected,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleId,
                        selectedRule.Id),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleCandidateCount,
                        applicableRules.Count),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleSelectionMode,
                        "stochastic")
                });
        }

        _lastRuleId = selectedRule.Id;
        return selectedRule.Action.Clone();
    }

    [TraceSpan]
    private async Task LearnUtilityAsync(float reward, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_lastRuleId is null || !_rules.TryGetValue(_lastRuleId, out var rule))
        {
            return;
        }

        rule.Utility += _learningRate * (reward - rule.Utility);
        await Task.CompletedTask;
    }
}
