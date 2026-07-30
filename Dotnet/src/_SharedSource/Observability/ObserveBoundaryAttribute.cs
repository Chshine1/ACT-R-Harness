using System.Diagnostics;
using System.Reflection;
using Harness.Abstractions.Observability;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Harness.Observability;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Module)]
[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public sealed class ObserveBoundaryAttribute : Attribute
{
    private ILogger? _logger;
    private MethodBase? _method;
    private object?[] _args = [];
    private Stopwatch? _stopwatch;
    private Activity? _activity;
    private bool _awaitsTaskContinuation;

    public void Init(object? instance, MethodBase method, object?[] args)
    {
        _logger = (instance as IProvideLogger)?.Logger;
        _method = method;
        _args = args;
    }

    public void OnEntry()
    {
        if (_logger is null || _method is null)
        {
            return;
        }

        _stopwatch = Stopwatch.StartNew();
        _activity = HarnessTelemetry.ActivitySource.StartActivity(GetBoundaryName());

        var context = HarnessExecutionContext.Current;
        _activity?.SetTag("run.id", context.RunId);
        _activity?.SetTag("run.epoch", context.Epoch);
        _activity?.SetTag("run.step", context.Step);
        _activity?.SetTag("run.correlation_id", context.CorrelationId);
        _activity?.SetTag("run.operation", context.Operation);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Boundary enter {Boundary} runId={RunId} epoch={Epoch} step={Step} correlationId={CorrelationId} args={Args}",
                GetBoundaryName(),
                context.RunId ?? "<none>",
                context.Epoch?.ToString() ?? "<none>",
                context.Step?.ToString() ?? "<none>",
                context.CorrelationId ?? "<none>",
                ObservabilityFormatter.Summarize(_args));
        }
    }

    public void OnExit()
    {
        if (_method is MethodInfo info && typeof(Task).IsAssignableFrom(info.ReturnType))
        {
            _awaitsTaskContinuation = true;
            return;
        }

        Complete();
    }

    public void OnException(Exception exception)
    {
        Complete(exception);
    }

    public void OnTaskContinuation(Task task)
    {
        if (!_awaitsTaskContinuation && task.Status != TaskStatus.RanToCompletion)
        {
            return;
        }

        Exception? failure = null;
        if (task.IsFaulted)
        {
            failure = task.Exception?.GetBaseException() ?? task.Exception;
        }
        else if (task.IsCanceled)
        {
            failure = new TaskCanceledException(task);
        }

        Complete(failure, task.Status);
    }

    private void Complete(Exception? exception = null, TaskStatus? taskStatus = null)
    {
        if (_logger is null || _method is null)
        {
            _activity?.Dispose();
            return;
        }

        var elapsedMs = Stop();
        _activity?.SetTag("boundary.elapsed_ms", elapsedMs);

        if (exception is null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Boundary exit {Boundary} elapsedMs={ElapsedMs} taskStatus={TaskStatus}",
                    GetBoundaryName(),
                    elapsedMs,
                    taskStatus?.ToString() ?? "<sync>");
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            _activity?.Dispose();
            return;
        }

        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(
                exception,
                "Boundary error {Boundary} elapsedMs={ElapsedMs}",
                GetBoundaryName(),
                elapsedMs);
        }

        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity?.Dispose();
    }

    private string GetBoundaryName()
    {
        return _method?.DeclaringType is { } declaringType
            ? $"{declaringType.Name}.{_method.Name}"
            : _method?.Name ?? "<unknown>";
    }

    private double Stop()
    {
        if (_stopwatch is null)
        {
            return 0;
        }

        _stopwatch.Stop();
        return _stopwatch.Elapsed.TotalMilliseconds;
    }
}