namespace Harness.Abstractions;

public record EpochContext(int Epoch);

public record StepContext(int Epoch, int Step, float Reward, bool Training);

public interface ITrainingLifecycle
{
    Task OnEpochStartedAsync(EpochContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task OnEpochCompletedAsync(EpochContext context, int stepCount, string stopReason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task OnStepStartedAsync(StepContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task OnStepCompletedAsync(StepContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}