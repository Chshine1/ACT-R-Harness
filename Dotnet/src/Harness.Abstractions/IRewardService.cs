namespace Harness.Abstractions;

public interface IRewardService
{
    Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default);
}