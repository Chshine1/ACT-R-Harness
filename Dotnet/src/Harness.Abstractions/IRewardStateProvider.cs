namespace Harness.Abstractions;

public interface IRewardStateProvider
{
    Type StateType { get; }
    Task<object> GetRewardStateAsync(CancellationToken cancellationToken = default);
}