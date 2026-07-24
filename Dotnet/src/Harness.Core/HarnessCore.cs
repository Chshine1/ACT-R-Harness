using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Modules;

namespace Harness.Core;

public class HarnessCore(IModuleRegistry moduleRegistry, IProceduralMemory proceduralMemory, INeuroCore neuro)
{
    private readonly IReadOnlyCollection<IModule> _modules = moduleRegistry.GetModules();

    public async Task<StepResult> StepAsync()
    {
        var bufferStates = _modules.Select(m => m.GetBufferState()).ToList();
        var schemas = _modules.Select(m => m.GetOperationSchema()).ToList();

        var conditions = proceduralMemory.GetAllConditions();
        var conditionResults = await neuro.EvaluateConditionsAsync(conditions, bufferStates);
        if (conditionResults.Count == 0)
        {
            return new StepResult(
                StepStopReason.NoApplicableRule,
                conditionResults.ToArray(),
                null,
                []);
        }

        var action = proceduralMemory.SelectRule(conditionResults);
        var operations = await neuro.DecodeActionAsync(action, bufferStates, schemas);

        foreach (var operation in operations)
        {
            var module = _modules.First(m => m.ModuleId == operation.TargetModuleId);
            module.OperateBuffer(operation);
        }

        return new StepResult(
            GetStopReason(),
            conditionResults.ToArray(),
            string.IsNullOrWhiteSpace(action.RuleId) ? null : action.RuleId,
            operations.ToArray());
    }

    private StepStopReason GetStopReason()
    {
        var intentionModule = _modules.FirstOrDefault(module => module.ModuleId == "intention");
        if (intentionModule is null)
        {
            return StepStopReason.Continue;
        }

        var state = intentionModule.GetBufferState().Data;
        if (!state.Fields.TryGetValue("current_goal", out var currentGoal) ||
            currentGoal.KindCase != Value.KindOneofCase.StructValue)
        {
            return StepStopReason.Continue;
        }

        var goalFields = currentGoal.StructValue.Fields;
        if (!goalFields.TryGetValue("slots", out var slots) ||
            slots.KindCase != Value.KindOneofCase.StructValue)
        {
            return StepStopReason.Continue;
        }

        var slotFields = slots.StructValue.Fields;
        if (!slotFields.TryGetValue("status", out var status) ||
            status.KindCase != Value.KindOneofCase.StringValue)
        {
            return StepStopReason.Continue;
        }

        return status.StringValue is "file_opened" or "done"
            ? StepStopReason.GoalReachedTerminalStatus
            : StepStopReason.Continue;
    }
}
