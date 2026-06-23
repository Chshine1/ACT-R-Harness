namespace Harness.Abstractions;

public record StepState(float Reward, bool Training);

public interface IClock
{
    delegate Task AsyncEventHandler<in TEventArgs>(TEventArgs e, CancellationToken cancellationToken);
    event AsyncEventHandler<StepState>? OnTick;
    Task TickAsync(StepState stepState, CancellationToken cancellationToken);
}