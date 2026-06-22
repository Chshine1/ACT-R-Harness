using Actr;

namespace Harness.Core;

public interface IProceduralMemory
{
    IReadOnlyList<ProceduralCondition> GetAllConditions();
    NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds);
    Task LearnUtilityAsync(string ruleId, float reward, CancellationToken cancellation = default);
}

public class ProceduralMemory: IProceduralMemory
{
    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        throw new NotImplementedException();
    }

    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        throw new NotImplementedException();
    }

    public Task LearnUtilityAsync(string ruleId, float reward, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }
}