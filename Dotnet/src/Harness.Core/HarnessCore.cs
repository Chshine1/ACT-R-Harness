using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Core;

public class HarnessCore(IModuleRegistry moduleRegistry, IProceduralMemory proceduralMemory, INeuroCore neuro)
{
    private readonly IReadOnlyCollection<IModule> _modules = moduleRegistry.GetModules();
    private readonly Dictionary<string, IModule> _modulesById = moduleRegistry.GetModules()
        .ToDictionary(module => module.ModuleId, StringComparer.OrdinalIgnoreCase);

    public async Task<StepResult> StepAsync(CancellationToken cancellationToken = default)
    {
        var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();
        var beforeSnapshots = bufferStatesBefore.Select(ToSnapshot).ToList();
        var satisfiedRuleIds = Array.Empty<string>();
        string? selectedRuleId = null;
        var operationTraces = new List<OperationTrace>();
        var diagnostics = new List<StepDiagnostic>
        {
            new(
                "capture_buffers_before",
                "Captured buffer snapshots before step execution.",
                new Dictionary<string, object?>
                {
                    ["moduleIds"] = beforeSnapshots.Select(snapshot => snapshot.ModuleId).ToArray(),
                    ["bufferCount"] = beforeSnapshots.Count
                })
        };
        var currentStage = "load_conditions";

        try
        {
            var conditions = proceduralMemory.GetAllConditions();
            diagnostics.Add(new StepDiagnostic(
                "load_conditions",
                "Loaded procedural rule conditions from procedural memory.",
                new Dictionary<string, object?>
                {
                    ["ruleCount"] = conditions.Count
                }));

            if (conditions.Count == 0)
            {
                return new StepResult(
                    BufferStatesBefore: beforeSnapshots,
                    BufferStatesAfter: beforeSnapshots,
                    SatisfiedRuleIds: [],
                    SelectedRuleId: null,
                    Operations: [],
                    IsTerminal: true,
                    StopReason: "no_rules_loaded",
                    Diagnostics: diagnostics);
            }

            var schemas = _modules.Select(module => module.GetOperationSchema()).ToList();
            diagnostics.Add(new StepDiagnostic(
                "load_schemas",
                "Collected module operation schemas for action decoding.",
                new Dictionary<string, object?>
                {
                    ["moduleIds"] = schemas.Select(schema => schema.ModuleId).ToArray(),
                    ["moduleCount"] = schemas.Count
                }));

            currentStage = "evaluate_conditions";
            satisfiedRuleIds = (await neuro.EvaluateConditionsAsync(
                conditions,
                bufferStatesBefore,
                cancellationToken)).ToArray();
            diagnostics.Add(new StepDiagnostic(
                "evaluate_conditions",
                "Evaluated rule conditions against current buffer state.",
                new Dictionary<string, object?>
                {
                    ["satisfiedRuleIds"] = satisfiedRuleIds,
                    ["satisfiedRuleCount"] = satisfiedRuleIds.Length
                }));

            if (satisfiedRuleIds.Length == 0)
            {
                return new StepResult(
                    BufferStatesBefore: beforeSnapshots,
                    BufferStatesAfter: beforeSnapshots,
                    SatisfiedRuleIds: satisfiedRuleIds,
                    SelectedRuleId: null,
                    Operations: [],
                    IsTerminal: true,
                    StopReason: "no_applicable_rule",
                    Diagnostics: diagnostics);
            }

            currentStage = "select_rule";
            var action = proceduralMemory.SelectRule(satisfiedRuleIds);
            selectedRuleId = action.RuleId;
            diagnostics.Add(new StepDiagnostic(
                "select_rule",
                "Selected a rule from the satisfied conflict set.",
                new Dictionary<string, object?>
                {
                    ["selectedRuleId"] = selectedRuleId,
                    ["candidateRuleIds"] = satisfiedRuleIds
                }));

            currentStage = "decode_action";
            var operations = await neuro.DecodeActionAsync(
                action,
                bufferStatesBefore,
                schemas,
                cancellationToken);
            operationTraces = operations.Select(ToTrace).ToList();
            diagnostics.Add(new StepDiagnostic(
                "decode_action",
                "Decoded the selected rule into concrete buffer operations.",
                new Dictionary<string, object?>
                {
                    ["operationCount"] = operationTraces.Count,
                    ["operations"] = operationTraces
                }));

            foreach (var operation in operations)
            {
                currentStage = $"apply_operation:{operation.TargetModuleId}.{operation.Command}";
                diagnostics.Add(new StepDiagnostic(
                    "apply_operation",
                    "Applying a decoded operation to a module buffer.",
                    new Dictionary<string, object?>
                    {
                        ["targetModuleId"] = operation.TargetModuleId,
                        ["command"] = operation.Command
                    }));

                if (!_modulesById.TryGetValue(operation.TargetModuleId, out var module))
                {
                    throw new InvalidOperationException(
                        $"No module registered for target '{operation.TargetModuleId}'.");
                }

                module.OperateBuffer(operation);
            }

            var bufferStatesAfter = _modules.Select(module => module.GetBufferState()).ToList();
            var afterSnapshots = bufferStatesAfter.Select(ToSnapshot).ToList();
            diagnostics.Add(new StepDiagnostic(
                "capture_buffers_after",
                "Captured buffer snapshots after step execution.",
                new Dictionary<string, object?>
                {
                    ["moduleIds"] = afterSnapshots.Select(snapshot => snapshot.ModuleId).ToArray(),
                    ["bufferCount"] = afterSnapshots.Count
                }));

            return new StepResult(
                BufferStatesBefore: beforeSnapshots,
                BufferStatesAfter: afterSnapshots,
                SatisfiedRuleIds: satisfiedRuleIds,
                SelectedRuleId: selectedRuleId,
                Operations: operationTraces,
                IsTerminal: false,
                StopReason: "rule_executed",
                Diagnostics: diagnostics);
        }
        catch (Exception ex)
        {
            var bufferStatesAfter = _modules.Select(module => module.GetBufferState()).ToList();
            diagnostics.Add(new StepDiagnostic(
                "error",
                "Step execution failed with an exception.",
                new Dictionary<string, object?>
                {
                    ["failureStage"] = currentStage,
                    ["errorType"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["errorMessage"] = ex.Message
                }));

            return new StepResult(
                BufferStatesBefore: beforeSnapshots,
                BufferStatesAfter: bufferStatesAfter.Select(ToSnapshot).ToList(),
                SatisfiedRuleIds: satisfiedRuleIds,
                SelectedRuleId: selectedRuleId,
                Operations: operationTraces,
                IsTerminal: true,
                StopReason: "error",
                Diagnostics: diagnostics,
                FailureStage: currentStage,
                ErrorType: ex.GetType().FullName ?? ex.GetType().Name,
                ErrorMessage: ex.Message,
                ErrorDetails: ex.ToString());
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
