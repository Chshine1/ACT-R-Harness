using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Host.Options;
using Harness.Shared.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class HarnessRunner(
    HarnessCore core,
    IEnumerable<ITrainingLifecycle> trainingLifecycles,
    IRewardService rewardService,
    IHostApplicationLifetime applicationLifetime,
    IOptions<HarnessOptions> options,
    ILogger<HarnessRunner> logger)
    : BackgroundService, IProvideLogger
{
    private readonly HarnessOptions _options = options.Value;
    private readonly IReadOnlyCollection<ITrainingLifecycle> _trainingLifecycles = trainingLifecycles.ToHashSet();

    public ILogger Logger => logger;

    [TraceSpan]
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var capturedEpoch = epoch;
                var lifecycles =
                    _trainingLifecycles.Select(t => t.OnEpochStartedAsync(new EpochContext(capturedEpoch), ct));
                await Task.WhenAll(lifecycles);

                await RunEpochAsync(ct);
            }
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    [TraceSpan]
    private async Task RunEpochAsync(CancellationToken ct)
    {
        for (var steps = 0; steps < _options.MaxStepsPerEpoch && !ct.IsCancellationRequested; steps++)
        {
            ct.ThrowIfCancellationRequested();

            var lastResult = await core.StepAsync(ct);

            if (lastResult.IsTerminal)
            {
                break;
            }

            await rewardService.ComputeRewardAsync(ct);

            await Task.Delay(10, ct);
        }
    }
}