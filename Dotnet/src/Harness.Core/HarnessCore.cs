using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Core;

public class HarnessCore(IModuleRegistry moduleRegistry, IProceduralMemory proceduralMemory, INeuroCore neuro)
{
    private readonly IReadOnlyCollection<IModule> _modules = moduleRegistry.GetModules();
    private readonly Dictionary<string, IModule> _modulesById = moduleRegistry.GetModules()
        .ToDictionary(module => module.ModuleId, StringComparer.OrdinalIgnoreCase);

    public async Task<StepResult> StepAsync()
    {
        var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();
        var beforeSnapshots = bufferStatesBefore.Select(ToSnapshot).ToList();
        var satisfiedRuleIds = Array.Empty<string>();
        string? selectedRuleId = null;

        try
        {
            var conditions = proceduralMemory.GetAllConditions();
            if (conditions.Count == 0)
            {
                return new StepResult(
                    beforeSnapshots,
                    beforeSnapshots,
                    Array.Empty<string>(),
                    null,
                    Array.Empty<OperationTrace>(),
                    true,
                    "no_rules_loaded");
            }

            var schemas = _modules.Select(module => module.GetOperationSchema()).ToList();
            satisfiedRuleIds = (await neuro.EvaluateConditionsAsync(conditions, bufferStatesBefore)).ToArray();
            if (satisfiedRuleIds.Length == 0)
            {
                return new StepResult(
                    beforeSnapshots,
                    beforeSnapshots,
                    satisfiedRuleIds,
                    null,
                    [],
                    true,
                    "no_applicable_rule");
            }

            var action = proceduralMemory.SelectRule(satisfiedRuleIds);
            selectedRuleId = action.RuleId;
            var operations = await neuro.DecodeActionAsync(action, bufferStatesBefore, schemas);

            foreach (var operation in operations)
            {
                if (!_modulesById.TryGetValue(operation.TargetModuleId, out var module))
                {
                    throw new InvalidOperationException(
                        $"No module registered for target '{operation.TargetModuleId}'.");
                }

                module.OperateBuffer(operation);
            }

            var bufferStatesAfter = _modules.Select(module => module.GetBufferState()).ToList();
            return new StepResult(
                beforeSnapshots,
                bufferStatesAfter.Select(ToSnapshot).ToList(),
                satisfiedRuleIds,
                selectedRuleId,
                operations.Select(ToTrace).ToList(),
                false,
                "rule_executed");
        }
        catch (Exception ex)
        {
            var bufferStatesAfter = _modules.Select(module => module.GetBufferState()).ToList();
            return new StepResult(
                beforeSnapshots,
                bufferStatesAfter.Select(ToSnapshot).ToList(),
                satisfiedRuleIds,
                selectedRuleId,
                Array.Empty<OperationTrace>(),
                true,
                "error",
                ex.Message);
        }
    }

    private static ModuleSnapshot ToSnapshot(BufferState state)
    {
        return new ModuleSnapshot(state.ModuleId, ProtobufDataConverter.ToPlainObjectMap(state.Data));
    }

    private static OperationTrace ToTrace(BufferOperation operation)
    {
        return new OperationTrace(
            operation.TargetModuleId,
            operation.Command,
            ProtobufDataConverter.ToPlainObjectMap(operation.Params));
    }
}
