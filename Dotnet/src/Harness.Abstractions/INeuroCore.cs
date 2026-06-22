using Harness.Abstractions.Actr;

namespace Harness.Abstractions;

public interface INeuroCore
{
    Task<IReadOnlyList<string>> EvaluateConditionsAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellation = default);

    Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellation = default);
}