using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Host.Options;
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
    IHostApplicationLifetime applicationLifetime,
    IOptions<HarnessOptions> options,
    ILogger<HarnessRunner> logger)
    : BackgroundService
{
    private readonly HarnessOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await WaitForDependenciesAsync(ct);

            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var session = artifactsWriter.StartRun(epoch);
                await scenarioSeeder.SeedAsync(ct);
                await RunEpochAsync(epoch, session, ct);
            }
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

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

    private async Task RunEpochAsync(
        int epoch,
        RunArtifactsSession session,
        CancellationToken cancellationToken)
    {
        var stopReason = "max_steps_reached";
        IReadOnlyList<ModuleSnapshot> finalBuffers = [];
        var stepCount = 0;
        var terminated = false;

        for (var step = 0; step < _options.MaxStepsPerEpoch && !cancellationToken.IsCancellationRequested; step++)
        {
            var result = await core.StepAsync();
            stepCount = step + 1;

            await artifactsWriter.AppendStepAsync(session, step, result, cancellationToken);
            logger.LogInformation(
                "Epoch {Epoch} step {Step}: rule={RuleId}, stop={StopReason}, ops={OperationCount}",
                epoch,
                step,
                result.SelectedRuleId ?? "<none>",
                result.StopReason,
                result.Operations.Count);

            stopReason = result.StopReason;
            finalBuffers = result.BufferStatesAfter;
            if (result.IsTerminal)
            {
                terminated = true;
                break;
            }

            var reward = await rewardService.ComputeRewardAsync(cancellationToken);
            await clock.TickAsync(new StepState(reward, _options.Training), cancellationToken);
            await Task.Delay(10, cancellationToken);
        }

        if (!terminated && stepCount > 0)
        {
            stopReason = "max_steps_reached";
        }

        await artifactsWriter.WriteSummaryAsync(session, stepCount, stopReason, finalBuffers, cancellationToken);
    }
}
