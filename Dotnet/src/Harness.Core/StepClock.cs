using Harness.Abstractions;

namespace Harness.Core;

public class StepClock : IClock, ITrainingLifecycle
{
    public event IClock.AsyncEventHandler<StepState>? OnTickAsync;

    public Task TickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        return OnTickAsync?.Invoke(stepState, cancellationToken) ?? Task.CompletedTask;
    }

    public async Task OnStepCompletedAsync(StepContext context, CancellationToken cancellationToken = default)
    {
        await TickAsync(new StepState(context.Reward, context.Training), cancellationToken);
    }
}