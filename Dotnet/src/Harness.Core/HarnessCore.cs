using Harness.Abstractions;
using Harness.Abstractions.Modules;

namespace Harness.Core;

public class HarnessCore(IModuleRegistry moduleRegistry, IProceduralMemory proceduralMemory, INeuroCore neuro)
{
    private readonly IReadOnlyCollection<IModule> _modules = moduleRegistry.GetModules();

    public async Task<bool> StepAsync()
    {
        var bufferStates = _modules.Select(m => m.GetBufferState()).ToList();
        var schemas = _modules.Select(m => m.GetOperationSchema()).ToList();

        var conditions = proceduralMemory.GetAllConditions();
        var conditionResults = await neuro.EvaluateConditionsAsync(conditions, bufferStates);

        var action = proceduralMemory.SelectRule(conditionResults);
        var operations = await neuro.DecodeActionAsync(action, bufferStates, schemas);

        foreach (var operation in operations)
        {
            var module = _modules.First(m => m.ModuleId == operation.TargetModuleId);
            module.OperateBuffer(operation);
        }

        return true;
    }
}