using Harness.Abstractions;
using Harness.Core.Modules;

namespace Harness.Core;

public class HouseholdRewardService(PerceptionMotorModule perceptionModule) : IRewardService
{
    public async Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default)
    {
        var state = (PerceptionRewardState)await perceptionModule.GetRewardStateAsync(cancellationToken);
        return ComputeReward(state);
    }

    private static float ComputeReward(PerceptionRewardState state)
    {
        double reward = 0;

        foreach (var room in state.Rooms)
        {
            var tempError = Math.Abs(room.Temperature - 22.0);
            reward -= tempError * 0.2;

            var humError = Math.Abs(room.Humidity - 50.0);
            reward -= humError * 0.05;

            if (room.AirQuality < 80)
                reward -= (80 - room.AirQuality) * 0.1;
        }

        reward -= state.TotalEnergy * 2.0;

        foreach (var room in state.Rooms)
            if (room.Temperature is > 35 or < 10)
                reward -= 100;

        return (float)reward;
    }
}