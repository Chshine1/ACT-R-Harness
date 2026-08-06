using Harness.Abstractions;
using Harness.Abstractions.Modules;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class HarnessCore(
    IEnumerable<IModule> modules,
    IProceduralMemory proceduralMemory,
    INeuroCore neuroCore,
    ILogger<HarnessCore> logger)
    : IProvideLogger
{
    private readonly IReadOnlyCollection<IModule> _modules = modules.ToHashSet();

    public ILogger Logger => logger;

    public async Task<StepResult> StepAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();
            LoggingModel.Log(
                logger,
                LogLevel.Debug,
                LoggingModel.Events.StepBuffers,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.BufferCount,
                        bufferStatesBefore.Count),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.BufferSnapshot,
                        bufferStatesBefore.ToDictionary(
                            state => state.ModuleId,
                            state => (object?)ProtobufDataConverter.ToPlainObjectMap(state.Data),
                            StringComparer.Ordinal))
                });

            var conditions = proceduralMemory.GetAllConditions();
            TracingModel.AddEvent(
                TracingModel.Events.ConditionsLoaded,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.ConditionCount,
                        conditions.Count)
                });
            LoggingModel.Log(
                logger,
                LogLevel.Debug,
                LoggingModel.Events.ConditionsLoaded,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ConditionCount,
                        conditions.Count)
                });

            if (conditions.Count == 0)
            {
                LoggingModel.Log(
                    logger,
                    LogLevel.Warning,
                    LoggingModel.Events.StepTerminated,
                    new[]
                    {
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.StopReason,
                            "no_rule_loaded"),
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.Terminal,
                            true),
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.Success,
                            true)
                    });
                return new StepResult(IsTerminal: true, StopReason: "no_rule_loaded");
            }

            var schemas = _modules.Select(module => module.GetOperationSchema()).ToList();

            var satisfiedRuleIds = (await neuroCore.EvaluateConditionsAsync(
                conditions,
                bufferStatesBefore,
                cancellationToken)).ToArray();
            TracingModel.AddEvent(
                TracingModel.Events.ConditionsEvaluated,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleSatisfiedCount,
                        satisfiedRuleIds.Length),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleSatisfiedIds,
                        string.Join(",", satisfiedRuleIds))
                });
            LoggingModel.Log(
                logger,
                LogLevel.Debug,
                LoggingModel.Events.ConditionsEvaluated,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.SatisfiedRuleIds,
                        satisfiedRuleIds),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.RuleCandidateCount,
                        conditions.Count)
                });

            if (satisfiedRuleIds.Length == 0)
            {
                LoggingModel.Log(
                    logger,
                    LogLevel.Warning,
                    LoggingModel.Events.StepTerminated,
                    new[]
                    {
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.StopReason,
                            "no_applicable_rule"),
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.Terminal,
                            true),
                        new KeyValuePair<string, object?>(
                            LoggingModel.Fields.Success,
                            true)
                    });
                return new StepResult(IsTerminal: true, StopReason: "no_applicable_rule");
            }

            var action = proceduralMemory.SelectRule(satisfiedRuleIds);

            var operations = await neuroCore.DecodeActionAsync(
                action,
                bufferStatesBefore,
                schemas,
                cancellationToken);
            TracingModel.AddEvent(
                TracingModel.Events.ActionDecoded,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.RuleId,
                        action.RuleId),
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.OperationCount,
                        operations.Count)
                });
            LoggingModel.Log(
                logger,
                LogLevel.Debug,
                LoggingModel.Events.ActionDecoded,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.RuleId,
                        action.RuleId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.OperationCount,
                        operations.Count),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.Operations,
                        operations.Select(OperationSummary).ToList())
                });

            var operationTraces = new List<OperationTrace>(operations.Count);
            foreach (var operation in operations)
            {
                if (_modules.FirstOrDefault(m => m.ModuleId == operation.TargetModuleId) is not { } module)
                {
                    throw new InvalidOperationException(
                        $"No module registered for target '{operation.TargetModuleId}'.");
                }

                module.OperateBuffer(operation);
                TracingModel.AddEvent(
                    TracingModel.Events.ModuleOperationApplied,
                    new[]
                    {
                        new KeyValuePair<string, object?>(
                            TracingModel.Tags.ModuleId,
                            operation.TargetModuleId),
                        new KeyValuePair<string, object?>(
                            TracingModel.Tags.OperationCommand,
                            operation.Command)
                    });
                operationTraces.Add(new OperationTrace(
                    operation.TargetModuleId,
                    operation.Command,
                    ProtobufDataConverter.ToPlainObjectMap(operation.Params)));
            }

            return new StepResult(
                IsTerminal: false,
                StopReason: "rule_executed",
                SelectedRuleId: action.RuleId,
                Operations: operationTraces);
        }
        catch (Exception exception)
        {
            TracingModel.RecordException(exception, nameof(StepAsync));
            LoggingModel.LogException(
                logger,
                nameof(StepAsync),
                exception,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.StopReason,
                        "error"),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.Terminal,
                        true),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.Success,
                        false)
                });
            return new StepResult(IsTerminal: true, StopReason: "error");
        }
    }

    private static Dictionary<string, object?> OperationSummary(BufferOperation operation) =>
        new(StringComparer.Ordinal)
        {
            ["module_id"] = operation.TargetModuleId,
            ["command"] = operation.Command,
            ["params"] = ProtobufDataConverter.ToPlainObjectMap(operation.Params)
        };
}
