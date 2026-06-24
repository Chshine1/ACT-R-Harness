namespace Harness.Abstractions.Reward;

public interface IRewardService
{
    Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default);
}