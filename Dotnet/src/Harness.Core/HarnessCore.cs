using Harness.Abstractions;

namespace Harness.Core;

public class HarnessCore(IModuleRegistry moduleRegistry, IProceduralMemory proceduralMemory, INeuroCore neuro)
{
    private string? _lastRuleId;

    public async Task StepAsync(float? reward)
    {
        if (reward.HasValue && _lastRuleId != null) await proceduralMemory.LearnUtilityAsync(_lastRuleId, reward.Value);

        var modules = moduleRegistry.GetModules();

        var bufferStates = modules.Select(m => m.GetBufferState()).ToList();
        var schemas = modules.Select(m => m.GetOperationSchema()).ToList();

        var conditions = proceduralMemory.GetAllConditions();
        var conditionResults = await neuro.EvaluateConditionsAsync(conditions, bufferStates);

        var actionIntent = proceduralMemory.SelectRule(conditionResults);
        _lastRuleId = actionIntent.RuleId;

        var ops = await neuro.DecodeActionAsync(actionIntent, bufferStates, schemas);

        foreach (var op in ops)
        {
            var module = modules.First(m => m.ModuleId == op.TargetModuleId);
            module.OperateBuffer(op);
        }
    }
}