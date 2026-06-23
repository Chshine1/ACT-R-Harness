using Harness.Abstractions;

namespace Harness.Core;

public class StepClock : IClock
{
    public event IClock.AsyncEventHandler<StepState>? OnTick;

    public Task TickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        return OnTick?.Invoke(stepState, cancellationToken) ?? Task.CompletedTask;
    }
}