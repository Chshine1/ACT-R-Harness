using System.Diagnostics;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Harness.Shared.Tests;

public sealed class LoggingModelTests
{
    [Fact]
    public void LogIncludesActivityCorrelationAndNormalizedFields()
    {
        using var listener = CreateListener();
        using var activity = TracingModel.StartActivity("Harness.Test");

        Assert.NotNull(activity);

        activity!.SetTag(TracingModel.Tags.RunId, "run-123");
        activity.SetTag(TracingModel.Tags.EpochIndex, 2);
        activity.SetTag(TracingModel.Tags.StepIndex, 7);

        var logger = new CaptureLogger();
        LoggingModel.Log(
            logger,
            LogLevel.Information,
            LoggingModel.Events.StepSummary,
            new[]
            {
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.RuleId,
                    "rule-1"),
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.StopReason,
                    "rule_executed")
            });

        var entry = Assert.Single(logger.Entries);
        var fields = entry.Fields.ToDictionary(field => field.Key, field => field.Value);

        Assert.Equal(LoggingModel.Events.StepSummary, fields[LoggingModel.Fields.EventName]);
        Assert.Equal("run-123", fields[LoggingModel.Fields.RunId]);
        Assert.Equal(activity.TraceId.ToString(), fields[LoggingModel.Fields.TraceId]);
        Assert.Equal(activity.SpanId.ToString(), fields[LoggingModel.Fields.SpanId]);
        Assert.Equal(2, fields[LoggingModel.Fields.Epoch]);
        Assert.Equal(7, fields[LoggingModel.Fields.Step]);
        Assert.Equal("rule-1", fields[LoggingModel.Fields.RuleId]);
        Assert.Equal("rule_executed", fields[LoggingModel.Fields.StopReason]);
    }

    [Fact]
    public void LogExceptionAddsNormalizedExceptionFields()
    {
        var logger = new CaptureLogger();
        var exception = new InvalidOperationException("logging failure");

        LoggingModel.LogException(
            logger,
            "harness.step",
            exception,
            new[]
            {
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.StopReason,
                    "error")
            });

        var entry = Assert.Single(logger.Entries);
        var fields = entry.Fields.ToDictionary(field => field.Key, field => field.Value);

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(LoggingModel.Events.Exception, fields[LoggingModel.Fields.EventName]);
        Assert.Equal("harness.step", fields[LoggingModel.Fields.Boundary]);
        Assert.Equal(typeof(InvalidOperationException).FullName, fields[LoggingModel.Fields.ExceptionType]);
        Assert.Equal("logging failure", fields[LoggingModel.Fields.ExceptionMessage]);
        Assert.Equal("error", fields[LoggingModel.Fields.StopReason]);
        Assert.Same(exception, entry.Exception);
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TracingModel.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state)
                .ToList();
            Entries.Add(new LogEntry(logLevel, fields, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        IReadOnlyList<KeyValuePair<string, object?>> Fields,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
