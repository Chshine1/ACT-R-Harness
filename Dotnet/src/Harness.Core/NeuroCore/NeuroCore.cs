using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core.NeuroCore;

public class NeuroCore(
    ConditionEvaluator conditionEvaluator,
    ActionResolver actionResolver,
    ILogger<NeuroCore> logger)
    : INeuroCore, IProvideLogger
{
    public ILogger Logger => logger;

    [ObserveBoundary]
    public async Task<IReadOnlyList<string>> EvaluateConditionsAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellationToken = default
    )
    {
        return await conditionEvaluator.EvaluateAsync(conditions, bufferStates, cancellationToken);
    }

    [ObserveBoundary]
    public async Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellationToken = default
    )
    {
        return await actionResolver.DecodeActionAsync(actionIntent, currentStates, schemas, cancellationToken);
    }
}
