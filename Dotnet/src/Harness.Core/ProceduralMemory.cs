using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core;

public class ProceduralMemory(Abstractions.Actr.Services.ProceduralMemory.ProceduralMemoryClient client)
    : IProceduralMemory
{
    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        var response = client.GetAllConditions(new Empty());
        return response.Conditions;
    }

    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        var response = client.SelectRule(
            new SelectRuleRequest
            {
                SatisfiedRuleIds = { satisfiedRuleIds }
            }
        );
        return response;
    }

    public async Task LearnUtilityAsync(string ruleId, float reward, CancellationToken cancellationToken = default)
    {
        await client.LearnUtilityAsync(
            new LearnUtilityRequest
            {
                RuleId = ruleId,
                Reward = reward
            }, cancellationToken: cancellationToken
        );
    }
}