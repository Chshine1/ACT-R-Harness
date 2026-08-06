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
        var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();

        try
        {
            var conditions = proceduralMemory.GetAllConditions();
            TracingModel.AddEvent(
                TracingModel.Events.ConditionsLoaded,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.ConditionCount,
                        conditions.Count)
                });

            if (conditions.Count == 0)
            {
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

            if (satisfiedRuleIds.Length == 0)
            {
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
            }

            return new StepResult(IsTerminal: false, StopReason: "rule_executed");
        }
        catch (Exception exception)
        {
            TracingModel.RecordException(exception, nameof(StepAsync));
            return new StepResult(IsTerminal: true, StopReason: "error");
        }
    }
}
