using Harness.Abstractions;

namespace Harness.Core;

public class StepClock : IClock
{
    public event IClock.AsyncEventHandler<float>? OnTick;

    public Task TickAsync(float reward, CancellationToken cancellationToken)
    {
        return OnTick?.Invoke(reward, cancellationToken) ?? Task.CompletedTask;
    }
}