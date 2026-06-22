using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core;

public class NeuroCore(Abstractions.Actr.Services.NeuroCore.NeuroCoreClient client) : INeuroCore
{
    public async Task<IReadOnlyList<string>> EvaluateConditionsAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellationToken = default
    )
    {
        var response = await client.EvaluateConditionsAsync(
            new EvaluateConditionsRequest
            {
                Conditions = { conditions },
                BufferStates = { bufferStates }
            },
            cancellationToken: cancellationToken
        );
        return response.SatisfiedRuleIds;
    }

    public async Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellationToken = default
    )
    {
        var response = await client.DecodeActionAsync(
            new DecodeActionRequest
            {
                ActionIntent = actionIntent,
                CurrentStates = { currentStates },
                Schemas = { schemas }
            },
            cancellationToken: cancellationToken
        );
        return response.Operations;
    }
}