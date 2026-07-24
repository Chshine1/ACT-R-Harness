using Harness.Abstractions;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public enum EpochStopReason
{
    NoApplicableRule,
    GoalReachedTerminalStatus,
    MaxStepsReached,
    Cancelled
}

public sealed record EpochResult(int StepsExecuted, EpochStopReason StopReason);

public class HarnessEpochExecutor(
    HarnessCore core,
    InitialGoalSeeder initialGoalSeeder,
    IClock clock,
    IRewardService rewardService,
    IOptions<HarnessOptions> options)
{
    private readonly HarnessOptions _options = options.Value;

    public async Task<EpochResult> RunAsync(CancellationToken cancellationToken)
    {
        initialGoalSeeder.Seed();

        var stepsExecuted = 0;
        while (stepsExecuted < _options.MaxStepsPerEpoch && !cancellationToken.IsCancellationRequested)
        {
            var result = await core.StepAsync();
            stepsExecuted++;

            if (result.SelectedRuleId is not null)
            {
                var reward = await rewardService.ComputeRewardAsync(cancellationToken);
                await clock.TickAsync(new StepState(reward, _options.Training), cancellationToken);
            }

            if (result.IsTerminal)
            {
                return new EpochResult(stepsExecuted, MapStopReason(result.StopReason));
            }

            try
            {
                await Task.Delay(10, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EpochResult(stepsExecuted, EpochStopReason.Cancelled);
            }
        }

        return cancellationToken.IsCancellationRequested
            ? new EpochResult(stepsExecuted, EpochStopReason.Cancelled)
            : new EpochResult(stepsExecuted, EpochStopReason.MaxStepsReached);
    }

    private static EpochStopReason MapStopReason(StepStopReason stopReason)
    {
        return stopReason switch
        {
            StepStopReason.NoApplicableRule => EpochStopReason.NoApplicableRule,
            StepStopReason.GoalReachedTerminalStatus => EpochStopReason.GoalReachedTerminalStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(stopReason), stopReason, null)
        };
    }
}
