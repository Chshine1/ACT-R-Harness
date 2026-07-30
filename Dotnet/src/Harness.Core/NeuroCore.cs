using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Harness.Abstractions.Observability;
using Harness.Core.Observability;
using Harness.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class NeuroCore(
    Abstractions.Actr.Services.NeuroCore.NeuroCoreClient client,
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
        using var call = GrpcObservabilityCall.Begin("neuro_core.evaluate_conditions");
        var response = await client.EvaluateConditionsAsync(
            new EvaluateConditionsRequest
            {
                Conditions = { conditions },
                BufferStates = { bufferStates }
            },
            headers: call.Headers,
            cancellationToken: cancellationToken
        );

        return response.SatisfiedRuleIds;
    }

    [ObserveBoundary]
    public async Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellationToken = default
    )
    {
        using var call = GrpcObservabilityCall.Begin("neuro_core.decode_action");
        var response = await client.DecodeActionAsync(
            new DecodeActionRequest
            {
                ActionIntent = actionIntent,
                CurrentStates = { currentStates },
                Schemas = { schemas }
            },
            headers: call.Headers,
            cancellationToken: cancellationToken
        );

        return response.Operations;
    }
}
