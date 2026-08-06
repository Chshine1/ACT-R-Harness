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
    private readonly string _runId = Guid.NewGuid().ToString("N");

    public ILogger Logger => logger;

    [TraceSpan(TracingModel.Spans.Run,
        "run.id = {this._runId}",
        "harness.max_epochs = {this._options.MaxEpochs}",
        "harness.max_steps_per_epoch = {this._options.MaxStepsPerEpoch}")]
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            TracingModel.AddEvent(
                TracingModel.Events.RunStarted,
                new[]
                {
                    new KeyValuePair<string, object?>(TracingModel.Tags.RunId, _runId)
                });

            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var capturedEpoch = epoch;
                var lifecycles =
                    _trainingLifecycles.Select(t => t.OnEpochStartedAsync(new EpochContext(capturedEpoch), ct));
                await Task.WhenAll(lifecycles);

                await RunEpochAsync(epoch, ct);
            }

            TracingModel.AddEvent(
                TracingModel.Events.RunCompleted,
                new[]
                {
                    new KeyValuePair<string, object?>(TracingModel.Tags.RunId, _runId)
                });
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    [TraceSpan("Harness.Epoch", "epoch.index={epochIndex}")]
    private async Task RunEpochAsync([UsedImplicitly] int epochIndex, CancellationToken ct)
    {
        TracingModel.AddEvent(
            TracingModel.Events.EpochStarted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex)
            });

        for (var steps = 0; steps < _options.MaxStepsPerEpoch && !ct.IsCancellationRequested; steps++)
        {
            ct.ThrowIfCancellationRequested();
            await RunSingleStepAsync(epochIndex, steps, ct);
        }

        TracingModel.AddEvent(
            TracingModel.Events.EpochCompleted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex)
            });
    }

    [TraceSpan(TracingModel.Spans.Step,
        "epoch.index = {epochIndex}",
        "step.index = {stepIndex}")]
    private async Task RunSingleStepAsync(
        [UsedImplicitly] int epochIndex,
        [UsedImplicitly] int stepIndex,
        CancellationToken ct)
    {
        TracingModel.AddEvent(
            TracingModel.Events.StepStarted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex),
                new KeyValuePair<string, object?>(TracingModel.Tags.StepIndex, stepIndex)
            });

        Activity.Current?.SetTag(TracingModel.Tags.EpochIndex, epochIndex);
        Activity.Current?.SetTag(TracingModel.Tags.StepIndex, stepIndex);
        var lastResult = await core.StepAsync(ct);

        if (lastResult.IsTerminal)
        {
            TracingModel.MarkTerminal(Activity.Current, lastResult.StopReason);
        }
        else
        {
            TracingModel.AddEvent(
                TracingModel.Events.StepCompleted,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.StopReason,
                        lastResult.StopReason)
                });
        }

        await rewardService.ComputeRewardAsync(ct);
        await clock.TickAsync(new StepState(Reward: 0, Training: true), ct);
        await Task.Delay(10, ct);
    }
}
