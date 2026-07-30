using Harness.Abstractions;
using Harness.Abstractions.Observability;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Core.Observability;
using Harness.Host.Options;
using Harness.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class HarnessRunner(
    HarnessCore core,
    IProceduralMemory proceduralMemory,
    IClock clock,
    IRewardService rewardService,
    DemoScenarioSeeder scenarioSeeder,
    RunArtifactsWriter artifactsWriter,
    IObservabilityEventSink eventSink,
    IHostApplicationLifetime applicationLifetime,
    IOptions<HarnessOptions> options,
    ILogger<HarnessRunner> logger)
    : BackgroundService, IProvideLogger
{
    private readonly HarnessOptions _options = options.Value;

    public ILogger Logger => logger;

    [ObserveBoundary]
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await WaitForDependenciesAsync(ct);

            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var session = artifactsWriter.StartRun(epoch);
                using var epochScope = HarnessExecutionContext.Push(runId: session.RunId, epoch: epoch);

                eventSink.Record(
                    "epoch.started",
                    LogLevel.Information,
                    "Started harness epoch.",
                    new Dictionary<string, object?>
                    {
                        ["scenarioName"] = session.ScenarioName,
                        ["runId"] = session.RunId
                    });

                await scenarioSeeder.SeedAsync(ct);
                eventSink.Record(
                    "scenario.seeded",
                    LogLevel.Information,
                    "Seeded initial scenario state.",
                    new Dictionary<string, object?>
                    {
                        ["goalId"] = _options.Scenario.GoalId,
                        ["goalStatus"] = _options.Scenario.GoalStatus,
                        ["workspaceRoot"] = Path.GetFullPath(_options.WorkspaceRoot)
                    });

                await RunEpochAsync(epoch, session, ct);
            }
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    [ObserveBoundary]
    private async Task WaitForDependenciesAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var conditions = proceduralMemory.GetAllConditions();
                if (conditions.Count == 0)
                {
                    throw new InvalidOperationException("Procedural memory returned zero rules.");
                }

                logger.LogInformation("Connected to Python services. Loaded {RuleCount} rules.", conditions.Count);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogInformation("Waiting for Python services: {Message}", ex.Message);
                await Task.Delay(1000, cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Timed out after {_options.StartupTimeoutSeconds} seconds waiting for Python services.",
            lastError);
    }

    [ObserveBoundary]
    private async Task RunEpochAsync(
        int epoch,
        RunArtifactsSession session,
        CancellationToken cancellationToken)
    {
        var stopReason = "max_steps_reached";
        StepResult? lastResult = null;
        var stepCount = 0;
        var terminated = false;

        for (var step = 0; step < _options.MaxStepsPerEpoch && !cancellationToken.IsCancellationRequested; step++)
        {
            using var stepScope = HarnessExecutionContext.Push(step: step + 1);

            var result = await core.StepAsync(cancellationToken);
            lastResult = result;
            stepCount = step + 1;
            stopReason = result.StopReason;

            if (result.IsTerminal)
            {
                terminated = true;
                break;
            }

            var reward = await rewardService.ComputeRewardAsync(cancellationToken);
            eventSink.Record(
                "reward.computed",
                LogLevel.Debug,
                "Computed reward after step execution.",
                new Dictionary<string, object?>
                {
                    ["reward"] = reward,
                    ["training"] = _options.Training
                });

            await clock.TickAsync(new StepState(reward, _options.Training), cancellationToken);
            eventSink.Record(
                "clock.ticked",
                LogLevel.Debug,
                "Advanced clock after reward update.",
                new Dictionary<string, object?>
                {
                    ["reward"] = reward,
                    ["training"] = _options.Training
                });

            await Task.Delay(10, cancellationToken);
        }

        if (!terminated && stepCount > 0)
        {
            stopReason = "max_steps_reached";
        }

        eventSink.Record(
            "epoch.completed",
            LogLevel.Information,
            "Completed harness epoch.",
            new Dictionary<string, object?>
            {
                ["epoch"] = epoch,
                ["totalSteps"] = stepCount,
                ["stopReason"] = stopReason,
                ["terminated"] = terminated,
                ["selectedRuleId"] = lastResult?.SelectedRuleId,
                ["finalBuffers"] = lastResult?.BufferStatesAfter ?? []
            });

        await artifactsWriter.WriteArtifactsAsync(
            session,
            stepCount,
            stopReason,
            lastResult,
            cancellationToken);
    }
}
