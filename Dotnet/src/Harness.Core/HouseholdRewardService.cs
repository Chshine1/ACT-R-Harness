using Harness.Abstractions.Reward;

namespace Harness.Core;

public record RoomSnapshot
{
    public double Temperature { get; init; }
    public double Humidity { get; init; }
    public double AirQuality { get; init; }
}

public record PerceptionRewardState
{
    public List<RoomSnapshot> Rooms { get; init; } = [];
    public double TotalEnergy { get; init; }
}

public class HouseholdRewardService : IRewardService
{
    public Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default)
    {
        var state = new PerceptionRewardState(); //await perceptionModule.GetRewardStateAsync(cancellationToken);
        return Task.FromResult(ComputeReward(state));
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