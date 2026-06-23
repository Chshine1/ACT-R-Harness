using Harness.Abstractions;
using Harness.Core.Modules;

namespace Harness.Core;

public class HouseholdRewardService(IModuleRegistry moduleRegistry): IRewardService
{
    public async Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default)
    {
        PerceptionRewardState? percept = null;

        foreach (var p in moduleRegistry.GetRewardProviders())
        {
            var state = await p.GetRewardStateAsync(cancellationToken);
            if (state is PerceptionRewardState ps) percept = ps;
        }
        
        return percept == null ? throw new InvalidOperationException() : 0f;
    }
}