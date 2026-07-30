namespace Harness.Abstractions.Observability;

public sealed record HarnessExecutionState(
    string? RunId = null,
    int? Epoch = null,
    int? Step = null,
    string? CorrelationId = null,
    string? Operation = null)
{
    public static HarnessExecutionState Empty { get; } = new();
}

public static class HarnessExecutionContext
{
    private static readonly AsyncLocal<HarnessExecutionState?> CurrentHolder = new();

    public static HarnessExecutionState Current => CurrentHolder.Value ?? HarnessExecutionState.Empty;

    public static IDisposable Push(
        string? runId = null,
        int? epoch = null,
        int? step = null,
        string? correlationId = null,
        string? operation = null)
    {
        var previous = Current;
        CurrentHolder.Value = new HarnessExecutionState(
            RunId: runId ?? previous.RunId,
            Epoch: epoch ?? previous.Epoch,
            Step: step ?? previous.Step,
            CorrelationId: correlationId ?? previous.CorrelationId,
            Operation: operation ?? previous.Operation);

        return new Scope(previous);
    }

    private sealed class Scope(HarnessExecutionState previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentHolder.Value = previous;
        }
    }
}