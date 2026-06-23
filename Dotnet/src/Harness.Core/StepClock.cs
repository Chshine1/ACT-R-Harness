using Harness.Abstractions;

namespace Harness.Core;

public class StepClock : IClock
{
    public event IClock.AsyncEventHandler<StepState>? OnTickAsync;

    public Task TickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        return OnTickAsync?.Invoke(stepState, cancellationToken) ?? Task.CompletedTask;
    }
}