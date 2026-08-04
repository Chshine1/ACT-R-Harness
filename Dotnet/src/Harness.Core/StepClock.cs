using Harness.Abstractions;
using Harness.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class StepClock(IObservabilityEventSink eventSink) : IClock, ITrainingLifecycle
{
    public event IClock.AsyncEventHandler<StepState>? OnTickAsync;

    public Task TickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        return OnTickAsync?.Invoke(stepState, cancellationToken) ?? Task.CompletedTask;
    }

    public async Task OnStepCompletedAsync(StepContext context, CancellationToken cancellationToken = default)
    {
        await TickAsync(new StepState(context.Reward, context.Training), cancellationToken);

        eventSink.Record(
            "clock.ticked",
            LogLevel.Debug,
            "Advanced clock after reward update.",
            new Dictionary<string, object?>
            {
                ["reward"] = context.Reward,
                ["training"] = context.Training
            });
    }
}