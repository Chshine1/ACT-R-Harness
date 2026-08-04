using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Core.Observability;
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
    RunArtifactsWriter artifactsWriter,
    IObservabilityEventSink eventSink,
    IHostApplicationLifetime applicationLifetime,
    IOptions<HarnessOptions> options,
    ILogger<HarnessRunner> logger)
    : BackgroundService, IProvideLogger
{
    private readonly HarnessOptions _options = options.Value;
    private readonly IReadOnlyCollection<ITrainingLifecycle> _trainingLifecycles = trainingLifecycles.ToHashSet();

    public ILogger Logger => logger;

    [ObserveBoundary]
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var session = artifactsWriter.StartRun(epoch);
                using var epochScope = HarnessExecutionContext.Push(runId: session.RunId, epoch: epoch);

                eventSink.Record(
                    "epoch.started",
                    LogLevel.Information,
                    "Started harness epoch.",
                    new
                    {
                        session.ScenarioName, session.RunId
                    });

                var capturedEpoch = epoch;
                var lifecycles =
                    _trainingLifecycles.Select(t => t.OnEpochStartedAsync(new EpochContext(capturedEpoch), ct));
                await Task.WhenAll(lifecycles);

                await RunEpochAsync(epoch, session, ct);
            }
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    [ObserveBoundary]
    private async Task RunEpochAsync(int epoch, RunArtifactsSession session, CancellationToken ct)
    {
        var steps = 0;
        StepResult? lastResult = null;
        var isTerminal = false;

        while (steps < _options.MaxStepsPerEpoch && !ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();

            using var stepScope = HarnessExecutionContext.Push(step: steps + 1);
            lastResult = await core.StepAsync(ct);
            steps++;

            if (lastResult.IsTerminal)
            {
                isTerminal = true;
                break;
            }

            var reward = await rewardService.ComputeRewardAsync(ct);
            eventSink.Record(
                "reward.computed",
                LogLevel.Debug,
                "Computed reward after step execution.",
                new { Reward = reward, _options.Training });

            await Task.Delay(10, ct);
        }

        var stopReason = isTerminal ? lastResult?.StopReason : "max_steps_reached";

        eventSink.Record(
            "epoch.completed",
            LogLevel.Information,
            "Completed harness epoch.",
            new
            {
                Epoch = epoch,
                TotalSteps = steps,
                StopReason = stopReason,
                Terminated = isTerminal,
                lastResult?.SelectedRuleId,
                FinalBuffers = lastResult?.BufferStatesAfter ?? []
            });

        await artifactsWriter.WriteArtifactsAsync(session, steps, stopReason ?? "<none>", lastResult, ct);
    }
}