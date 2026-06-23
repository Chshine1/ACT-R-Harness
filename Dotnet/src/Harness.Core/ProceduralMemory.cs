using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core;

public class ProceduralMemory : IProceduralMemory
{
    private string? _lastRuleId;
    private readonly Abstractions.Actr.Services.ProceduralMemory.ProceduralMemoryClient _client;

    public ProceduralMemory(Abstractions.Actr.Services.ProceduralMemory.ProceduralMemoryClient client, IClock clock)
    {
        _client = client;
        clock.OnTick += (reward, ct) =>
        {
            if (!reward.Training) return Task.CompletedTask;
            return _lastRuleId == null ? throw new InvalidOperationException() : LearnUtilityAsync(reward.Reward, ct);
        };
    }

    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        var response = _client.GetAllConditions(new Empty());
        return response.Conditions;
    }

    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        var response = _client.SelectRule(
            new SelectRuleRequest
            {
                SatisfiedRuleIds = { satisfiedRuleIds }
            }
        );
        _lastRuleId = response.RuleId;
        return response;
    }

    private async Task LearnUtilityAsync(float reward, CancellationToken cancellationToken = default)
    {
        await _client.LearnUtilityAsync(
            new LearnUtilityRequest
            {
                RuleId = _lastRuleId,
                Reward = reward
            }, cancellationToken: cancellationToken
        );
    }
}