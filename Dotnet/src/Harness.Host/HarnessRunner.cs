using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Host.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class HarnessRunner(
    HarnessCore core,
    IClock clock,
    IRewardService rewardService,
    IOptions<HarnessOptions> options)
    : BackgroundService
{
    private readonly HarnessOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        for (var epoch = 0; epoch < _options.MaxEpochs; epoch++) await RunEpochAsync(epoch, ct);
    }

    private async Task RunEpochAsync(int _, CancellationToken cancellationToken)
    {
        var isTerminal = false;
        var step = 0;
        while (!isTerminal && step < _options.MaxStepsPerEpoch && !cancellationToken.IsCancellationRequested)
        {
            isTerminal = await core.StepAsync();
            var reward = await rewardService.ComputeRewardAsync(cancellationToken);
            var state = new StepState(reward, _options.Training);
            await clock.TickAsync(state, cancellationToken);
            step++;
            await Task.Delay(10, cancellationToken);
        }
    }
}