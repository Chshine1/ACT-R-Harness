using System.Diagnostics;
using Harness.Shared.Observability;
using Xunit;

namespace Harness.Shared.Tests;

public sealed class TracingModelTests
{
    [Fact]
    public void RecordExceptionAddsSemanticExceptionEventAndErrorStatus()
    {
        using var listener = CreateListener();
        using var activity = TracingModel.StartActivity("Harness.Test");

        Assert.NotNull(activity);

        var exception = new InvalidOperationException("trace failure");
        TracingModel.RecordException(activity, exception, "test.boundary");

        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.Equal("trace failure", activity.StatusDescription);

        var exceptionEvent = Assert.Single(activity.Events, traceEvent => traceEvent.Name == "exception");
        var tags = exceptionEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);

        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.Equal("trace failure", tags["exception.message"]);
        Assert.Equal("test.boundary", tags["observability.boundary"]);
        Assert.True(tags.ContainsKey("exception.stacktrace"));
    }

    [Fact]
    public void RecordCancellationDoesNotCreateExceptionOrErrorStatus()
    {
        using var listener = CreateListener();
        using var activity = TracingModel.StartActivity("Harness.Test");

        Assert.NotNull(activity);

        TracingModel.RecordCancellation(activity);

        Assert.NotEqual(ActivityStatusCode.Error, activity!.Status);
        Assert.DoesNotContain(activity.Events, traceEvent => traceEvent.Name == "exception");
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
}
