using System.Diagnostics;
using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Host.Options;
using Harness.Shared.Observability;
using JetBrains.Annotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class HarnessRunner(
    HarnessCore core,
    IClock clock,
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

    [TraceSpan("TrainingSession",
        "harness.max_epochs = {this._options.MaxEpochs}",
        "harness.max_steps_per_epoch = {this._options.MaxStepsPerEpoch}")]
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

                await RunEpochAsync(epoch, ct);
            }
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    [TraceSpan("Harness.Epoch", "epoch.index={epochIndex}")]
    private async Task RunEpochAsync([UsedImplicitly] int epochIndex, CancellationToken ct)
    {
        for (var steps = 0; steps < _options.MaxStepsPerEpoch && !ct.IsCancellationRequested; steps++)
        {
            ct.ThrowIfCancellationRequested();
            await RunSingleStepAsync(steps, ct);
        }
    }

    [TraceSpan("Harness.Step", "step.index={stepIndex}")]
    private async Task RunSingleStepAsync([UsedImplicitly] int stepIndex, CancellationToken ct)
    {
        var lastResult = await core.StepAsync(ct);

        if (lastResult.IsTerminal)
        {
            Activity.Current?.AddEvent(new ActivityEvent("step.terminated",
                tags: new ActivityTagsCollection
                {
                    { "step.stop_reason", lastResult.StopReason }
                }));
        }

        await rewardService.ComputeRewardAsync(ct);
        await clock.TickAsync(new StepState(Reward: 0, Training: true), ct);
        await Task.Delay(10, ct);
    }
}