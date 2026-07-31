using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Harness.Core.Observability;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class HarnessCore(
    IModuleRegistry moduleRegistry,
    IProceduralMemory proceduralMemory,
    INeuroCore neuro,
    IObservabilityEventSink eventSink,
    ILogger<HarnessCore> logger)
    : IProvideLogger
{
    private readonly IReadOnlyCollection<IModule> _modules = moduleRegistry.GetModules();
    private readonly Dictionary<string, IModule> _modulesById = moduleRegistry.GetModules()
        .ToDictionary(module => module.ModuleId, StringComparer.OrdinalIgnoreCase);

    public ILogger Logger => logger;

    [ObserveBoundary]
    public async Task<StepResult> StepAsync(CancellationToken cancellationToken = default)
    {
        var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();
        var beforeSnapshots = bufferStatesBefore.Select(ToSnapshot).ToList();
        var satisfiedRuleIds = Array.Empty<string>();
        string? selectedRuleId = null;
        var operationTraces = new List<OperationTrace>();
        var currentStage = "load_conditions";
        var neuroInvolved = false;

        eventSink.Record(
            "step.started",
            LogLevel.Debug,
            "Captured buffer states before step execution.",
            new Dictionary<string, object?>
            {
                ["bufferStatesBefore"] = beforeSnapshots,
                ["bufferCount"] = beforeSnapshots.Count,
                ["moduleIds"] = beforeSnapshots.Select(snapshot => snapshot.ModuleId).ToArray()
            });

        try
        {
            var conditions = proceduralMemory.GetAllConditions();
            eventSink.Record(
                "rule.conditions_loaded",
                LogLevel.Debug,
                "Loaded procedural rule conditions from procedural memory.",
                new Dictionary<string, object?>
                {
                    ["ruleCount"] = conditions.Count,
                    ["semanticConditionCount"] = conditions.Count(condition => condition.Semantics.Fields.Count > 0)
                });

            if (conditions.Count == 0)
            {
                return CompleteStep(
                    stopReason: "no_rules_loaded",
                    beforeSnapshots,
                    beforeSnapshots,
                    satisfiedRuleIds,
                    selectedRuleId,
                    operationTraces,
                    isTerminal: true,
                    neuroInvolved: neuroInvolved);
            }

            var schemas = _modules.Select(module => module.GetOperationSchema()).ToList();
            eventSink.Record(
                "action.schemas_loaded",
                LogLevel.Debug,
                "Collected module command schemas for action decoding.",
                new Dictionary<string, object?>
                {
                    ["moduleIds"] = schemas.Select(schema => schema.ModuleId).ToArray(),
                    ["moduleCount"] = schemas.Count
                });

            currentStage = "evaluate_conditions";
            satisfiedRuleIds = (await neuro.EvaluateConditionsAsync(
                conditions,
                bufferStatesBefore,
                cancellationToken)).ToArray();

            eventSink.Record(
                "rule.conflict_set",
                LogLevel.Information,
                "Evaluated rule conditions against the current buffer state.",
                new Dictionary<string, object?>
                {
                    ["satisfiedRuleIds"] = satisfiedRuleIds,
                    ["satisfiedRuleCount"] = satisfiedRuleIds.Length
                });

            if (satisfiedRuleIds.Length == 0)
            {
                return CompleteStep(
                    stopReason: "no_applicable_rule",
                    beforeSnapshots,
                    beforeSnapshots,
                    satisfiedRuleIds,
                    selectedRuleId,
                    operationTraces,
                    isTerminal: true,
                    neuroInvolved: neuroInvolved);
            }

            currentStage = "select_rule";
            var action = proceduralMemory.SelectRule(satisfiedRuleIds);
            selectedRuleId = action.RuleId;
            neuroInvolved = action.Semantics.Count > 0;

            eventSink.Record(
                "rule.selected",
                LogLevel.Information,
                "Selected a rule from the satisfied conflict set.",
                new Dictionary<string, object?>
                {
                    ["selectedRuleId"] = selectedRuleId,
                    ["candidateRuleIds"] = satisfiedRuleIds,
                    ["commandAliases"] = action.Commands.Keys.ToArray(),
                    ["semanticKeys"] = action.Semantics.Keys.ToArray(),
                    ["neuroInvolved"] = neuroInvolved
                });

            currentStage = "decode_action";
            var operations = await neuro.DecodeActionAsync(
                action,
                bufferStatesBefore,
                schemas,
                cancellationToken);

            operationTraces = operations.Select(ToTrace).ToList();
            eventSink.Record(
                "action.decoded",
                LogLevel.Information,
                "Decoded the selected rule into concrete buffer operations.",
                new Dictionary<string, object?>
                {
                    ["selectedRuleId"] = selectedRuleId,
                    ["operationCount"] = operationTraces.Count,
                    ["operations"] = operationTraces,
                    ["neuroInvolved"] = neuroInvolved
                });

            foreach (var operation in operations)
            {
                currentStage = $"apply_operation:{operation.TargetModuleId}.{operation.Command}";
                if (!_modulesById.TryGetValue(operation.TargetModuleId, out var module))
                {
                    throw new InvalidOperationException(
                        $"No module registered for target '{operation.TargetModuleId}'.");
                }

                var moduleBefore = ToSnapshot(module.GetBufferState());
                module.OperateBuffer(operation);
                var moduleAfter = ToSnapshot(module.GetBufferState());
                var moduleChanges = BufferDiffBuilder.Build(moduleBefore.Data, moduleAfter.Data);

                eventSink.Record(
                    "buffer_operation.applied",
                    LogLevel.Debug,
                    "Applied a decoded operation to a module buffer.",
                    new Dictionary<string, object?>
                    {
                        ["selectedRuleId"] = selectedRuleId,
                        ["targetModuleId"] = operation.TargetModuleId,
                        ["command"] = operation.Command,
                        ["params"] = ToTrace(operation).Params,
                        ["bufferBefore"] = moduleBefore,
                        ["bufferAfter"] = moduleAfter,
                        ["bufferChanges"] = moduleChanges
                    });
            }

            var afterSnapshots = _modules.Select(module => module.GetBufferState())
                .Select(ToSnapshot)
                .ToList();

            return CompleteStep(
                stopReason: "rule_executed",
                beforeSnapshots,
                afterSnapshots,
                satisfiedRuleIds,
                selectedRuleId,
                operationTraces,
                isTerminal: false,
                neuroInvolved: neuroInvolved);
        }
        catch (Exception ex)
        {
            var afterSnapshots = _modules.Select(module => module.GetBufferState())
                .Select(ToSnapshot)
                .ToList();
            var bufferChanges = BufferDiffBuilder.Build(beforeSnapshots, afterSnapshots);
            var failureData = new Dictionary<string, object?>
            {
                ["stopReason"] = "error",
                ["failureStage"] = currentStage,
                ["errorSummary"] = ExceptionDetailsFormatter.BuildSummary(ex),
                ["selectedRuleId"] = selectedRuleId,
                ["satisfiedRuleIds"] = satisfiedRuleIds,
                ["operations"] = operationTraces,
                ["bufferStatesBefore"] = beforeSnapshots,
                ["bufferStatesAfter"] = afterSnapshots,
                ["bufferChanges"] = bufferChanges
            };

            foreach (var entry in ExceptionDetailsFormatter.ToDictionary(ex))
            {
                failureData[entry.Key] = entry.Value;
            }

            eventSink.Record(
                "step.failed",
                LogLevel.Error,
                "Step execution failed with an exception.",
                failureData);

            return new StepResult(
                BufferStatesBefore: beforeSnapshots,
                BufferStatesAfter: afterSnapshots,
                SatisfiedRuleIds: satisfiedRuleIds,
                SelectedRuleId: selectedRuleId,
                Operations: operationTraces,
                BufferChanges: bufferChanges,
                IsTerminal: true,
                StopReason: "error",
                FailureStage: currentStage,
                ErrorType: ex.GetType().FullName ?? ex.GetType().Name,
                ErrorMessage: ex.Message,
                ErrorDetails: ex.ToString());
        }
    }

    private StepResult CompleteStep(
        string stopReason,
        IReadOnlyList<ModuleSnapshot> beforeSnapshots,
        IReadOnlyList<ModuleSnapshot> afterSnapshots,
        IReadOnlyList<string> satisfiedRuleIds,
        string? selectedRuleId,
        IReadOnlyList<OperationTrace> operationTraces,
        bool isTerminal,
        bool neuroInvolved)
    {
        var bufferChanges = BufferDiffBuilder.Build(beforeSnapshots, afterSnapshots);

        eventSink.Record(
            "step.completed",
            LogLevel.Debug,
            "Completed step execution.",
            new Dictionary<string, object?>
            {
                ["stopReason"] = stopReason,
                ["isTerminal"] = isTerminal,
                ["selectedRuleId"] = selectedRuleId,
                ["satisfiedRuleIds"] = satisfiedRuleIds,
                ["operations"] = operationTraces,
                ["bufferStatesBefore"] = beforeSnapshots,
                ["bufferStatesAfter"] = afterSnapshots,
                ["bufferChanges"] = bufferChanges,
                ["neuroInvolved"] = neuroInvolved
            });

        return new StepResult(
            BufferStatesBefore: beforeSnapshots,
            BufferStatesAfter: afterSnapshots,
            SatisfiedRuleIds: satisfiedRuleIds,
            SelectedRuleId: selectedRuleId,
            Operations: operationTraces,
            BufferChanges: bufferChanges,
            IsTerminal: isTerminal,
            StopReason: stopReason);
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
