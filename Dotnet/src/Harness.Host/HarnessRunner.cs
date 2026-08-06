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
    ILogger<HarnessRunner> logger,
    RunReportWriter reportWriter)
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
        var reportBuilder = new RunReportBuilder(_runId, DateTimeOffset.UtcNow);
        var status = "completed";
        var stopReason = "completed";

        try
        {
            TracingModel.AddEvent(
                TracingModel.Events.RunStarted,
                new[]
                {
                    new KeyValuePair<string, object?>(TracingModel.Tags.RunId, _runId)
                });
            LoggingModel.Log(
                logger,
                LogLevel.Information,
                LoggingModel.Events.RunStarted,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.RunId, _runId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.MaxEpochs,
                        _options.MaxEpochs),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.MaxStepsPerEpoch,
                        _options.MaxStepsPerEpoch)
                });

            for (var epoch = 0; epoch < _options.MaxEpochs; epoch++)
            {
                var capturedEpoch = epoch;
                var lifecycles =
                    _trainingLifecycles.Select(t => t.OnEpochStartedAsync(new EpochContext(capturedEpoch), ct));
                await Task.WhenAll(lifecycles);

                var terminalResult = await RunEpochAsync(epoch, ct, reportBuilder);
                if (terminalResult is null)
                {
                    continue;
                }

                stopReason = terminalResult.StopReason;
                status = terminalResult.StopReason == "error"
                    ? "failed"
                    : "completed";
                break;
            }

            Activity.Current?.SetTag(TracingModel.Tags.StopReason, stopReason);
            if (status == "failed")
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, stopReason);
            }

            TracingModel.AddEvent(
                TracingModel.Events.RunCompleted,
                new[]
                {
                    new KeyValuePair<string, object?>(TracingModel.Tags.RunId, _runId)
                });
            LoggingModel.Log(
                logger,
                LogLevel.Information,
                LoggingModel.Events.RunCompleted,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.RunId, _runId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ReportStatus,
                        status),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.StopReason,
                        stopReason),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.Success,
                        status == "completed")
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = "canceled";
            stopReason = "canceled";
            LoggingModel.Log(
                logger,
                LogLevel.Information,
                LoggingModel.Events.RunCompleted,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.RunId, _runId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ReportStatus,
                        status),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.StopReason,
                        stopReason),
                    new KeyValuePair<string, object?>(LoggingModel.Fields.Success, false)
                });
            throw;
        }
        catch (Exception exception)
        {
            status = "failed";
            stopReason = "error";
            reportBuilder.RecordFailure(
                FailureReport.FromException(exception, nameof(ExecuteAsync)));
            LoggingModel.LogException(
                logger,
                nameof(ExecuteAsync),
                exception,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.RunId, _runId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ReportStatus,
                        status),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.StopReason,
                        stopReason),
                    new KeyValuePair<string, object?>(LoggingModel.Fields.Success, false)
                });
            throw;
        }
        finally
        {
            Activity.Current?.SetTag(TracingModel.Tags.StopReason, stopReason);
            reportWriter.Write(
                reportBuilder.Build(
                    status,
                    stopReason,
                    Activity.Current,
                    DateTimeOffset.UtcNow),
                _options.ArtifactRoot);
            applicationLifetime.StopApplication();
        }
    }

    [TraceSpan("Harness.Epoch", "epoch.index={epochIndex}")]
    private async Task<StepResult?> RunEpochAsync(
        [UsedImplicitly] int epochIndex,
        CancellationToken ct,
        RunReportBuilder reportBuilder)
    {
        TracingModel.AddEvent(
            TracingModel.Events.EpochStarted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex)
            });
        LoggingModel.Log(
            logger,
            LogLevel.Information,
            LoggingModel.Events.EpochStarted,
            new[]
            {
                new KeyValuePair<string, object?>(LoggingModel.Fields.Epoch, epochIndex)
            });

        StepResult? terminalResult = null;
        for (var steps = 0; steps < _options.MaxStepsPerEpoch && !ct.IsCancellationRequested; steps++)
        {
            ct.ThrowIfCancellationRequested();
            var stepResult = await RunSingleStepAsync(epochIndex, steps, ct, reportBuilder);
            if (stepResult.IsTerminal)
            {
                terminalResult = stepResult;
                break;
            }
        }

        TracingModel.AddEvent(
            TracingModel.Events.EpochCompleted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex)
            });
        LoggingModel.Log(
            logger,
            LogLevel.Information,
            LoggingModel.Events.EpochCompleted,
            new[]
            {
                new KeyValuePair<string, object?>(LoggingModel.Fields.Epoch, epochIndex)
            });
        return terminalResult;
    }

    [TraceSpan(TracingModel.Spans.Step,
        "epoch.index = {epochIndex}",
        "step.index = {stepIndex}")]
    private async Task<StepResult> RunSingleStepAsync(
        [UsedImplicitly] int epochIndex,
        [UsedImplicitly] int stepIndex,
        CancellationToken ct,
        RunReportBuilder reportBuilder)
    {
        TracingModel.AddEvent(
            TracingModel.Events.StepStarted,
            new[]
            {
                new KeyValuePair<string, object?>(TracingModel.Tags.EpochIndex, epochIndex),
                new KeyValuePair<string, object?>(TracingModel.Tags.StepIndex, stepIndex)
            });
        LoggingModel.Log(
            logger,
            LogLevel.Debug,
            LoggingModel.Events.StepStarted,
            new[]
            {
                new KeyValuePair<string, object?>(LoggingModel.Fields.Epoch, epochIndex),
                new KeyValuePair<string, object?>(LoggingModel.Fields.Step, stepIndex)
            });

        Activity.Current?.SetTag(TracingModel.Tags.EpochIndex, epochIndex);
        Activity.Current?.SetTag(TracingModel.Tags.StepIndex, stepIndex);
        reportBuilder.SetCurrentStep(epochIndex, stepIndex);
        var lastResult = await core.StepAsync(ct);
        var operations = lastResult.Operations ?? Array.Empty<OperationTrace>();
        reportBuilder.RecordStep(
            epochIndex,
            stepIndex,
            lastResult.SelectedRuleId,
            lastResult.StopReason,
            success: lastResult.StopReason != "error");
        if (lastResult.Failure is not null)
        {
            reportBuilder.RecordFailure(lastResult.Failure, epochIndex, stepIndex);
        }
        LoggingModel.Log(
            logger,
            LogLevel.Information,
            LoggingModel.Events.StepSummary,
            new[]
            {
                new KeyValuePair<string, object?>(LoggingModel.Fields.Epoch, epochIndex),
                new KeyValuePair<string, object?>(LoggingModel.Fields.Step, stepIndex),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.RuleId,
                    lastResult.SelectedRuleId),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.OperationCount,
                    operations.Count),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.Operations,
                    operations),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.StopReason,
                    lastResult.StopReason),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.Terminal,
                    lastResult.IsTerminal),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.Success,
                    lastResult.StopReason != "error")
            });

        if (lastResult.IsTerminal)
        {
            TracingModel.MarkTerminal(Activity.Current, lastResult.StopReason);
            return lastResult;
        }

        TracingModel.AddEvent(
            TracingModel.Events.StepCompleted,
            new[]
            {
                new KeyValuePair<string, object?>(
                    TracingModel.Tags.StopReason,
                    lastResult.StopReason)
            });

        await rewardService.ComputeRewardAsync(ct);
        await clock.TickAsync(new StepState(Reward: 0, Training: true), ct);
        await Task.Delay(10, ct);
        return lastResult;
    }
}
