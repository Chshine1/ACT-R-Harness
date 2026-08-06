using System.Diagnostics;

namespace Harness.Shared.Observability;

public sealed class RunReportBuilder
{
    public const string SchemaVersion = "1.0";

    private readonly string _runId;
    private readonly DateTimeOffset _startedAtUtc;
    private SuccessfulStepReport? _lastSuccessfulStep;
    private FailureReport? _failure;
    private int? _currentEpoch;
    private int? _currentStep;

    public RunReportBuilder(string runId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        _runId = runId;
        _startedAtUtc = startedAtUtc;
    }

    public void SetCurrentStep(int epoch, int step)
    {
        _currentEpoch = epoch;
        _currentStep = step;
    }

    public void RecordStep(
        int epoch,
        int step,
        string? ruleId,
        string stopReason,
        bool success)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stopReason);
        SetCurrentStep(epoch, step);

        if (success)
        {
            _lastSuccessfulStep = new SuccessfulStepReport(epoch, step, ruleId, stopReason);
        }
    }

    public void RecordFailure(FailureReport failure, int? epoch = null, int? step = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (_failure is not null)
        {
            return;
        }

        _failure = failure with
        {
            Epoch = epoch ?? failure.Epoch ?? _currentEpoch,
            Step = step ?? failure.Step ?? _currentStep
        };
    }

    public RunReport Build(
        string status,
        string stopReason,
        Activity? activity,
        DateTimeOffset completedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(stopReason);

        return new RunReport(
            SchemaVersion,
            _runId,
            status,
            stopReason,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            _startedAtUtc,
            completedAtUtc,
            _lastSuccessfulStep,
            _failure);
    }
}
