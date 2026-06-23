namespace Harness.Abstractions;

public interface IClock
{
    delegate Task AsyncEventHandler<in TEventArgs>(TEventArgs e, CancellationToken cancellationToken);
    event AsyncEventHandler<float>? OnTick;
    Task TickAsync(float reward, CancellationToken cancellationToken);
}