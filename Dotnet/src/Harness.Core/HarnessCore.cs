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

    [TraceSpan]
    public async Task<StepResult> StepAsync(CancellationToken cancellationToken = default)
    {
        var bufferStatesBefore = _modules.Select(module => module.GetBufferState()).ToList();

        try
        {
            var conditions = proceduralMemory.GetAllConditions();

            if (conditions.Count == 0)
            {
                return new StepResult(IsTerminal: true, StopReason: "no_rule_loaded");
            }

            var schemas = _modules.Select(module => module.GetOperationSchema()).ToList();

            var satisfiedRuleIds = (await neuroCore.EvaluateConditionsAsync(
                conditions,
                bufferStatesBefore,
                cancellationToken)).ToArray();

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

            foreach (var operation in operations)
            {
                if (_modules.FirstOrDefault(m => m.ModuleId == operation.TargetModuleId) is not { } module)
                {
                    throw new InvalidOperationException(
                        $"No module registered for target '{operation.TargetModuleId}'.");
                }

                module.OperateBuffer(operation);
            }

            return new StepResult(IsTerminal: false, StopReason: "rule_executed");
        }
        catch (Exception)
        {
            return new StepResult(IsTerminal: true, StopReason: "error");
        }
    }
}