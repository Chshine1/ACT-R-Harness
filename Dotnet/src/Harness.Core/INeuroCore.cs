using Actr;

namespace Harness.Core;

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

public class NeuroCore : INeuroCore
{
    public Task<IReadOnlyList<string>> EvaluateConditionsAsync(IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates, IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }
}