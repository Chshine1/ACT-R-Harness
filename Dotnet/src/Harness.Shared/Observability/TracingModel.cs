using System.Diagnostics;

namespace Harness.Shared.Observability;

public static class TracingModel
{
    public const string ActivitySourceName = "ACTR.Harness";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    public static class Spans
    {
        public const string Run = "Harness.Run";
        public const string Epoch = "Harness.Epoch";
        public const string Step = "Harness.Step";
    }

    public static class Events
    {
        public const string RunStarted = "harness.run.started";
        public const string RunCompleted = "harness.run.completed";
        public const string EpochStarted = "harness.epoch.started";
        public const string EpochCompleted = "harness.epoch.completed";
        public const string StepStarted = "harness.step.started";
        public const string ConditionsLoaded = "harness.conditions.loaded";
        public const string ConditionsEvaluated = "harness.conditions.evaluated";
        public const string RuleSelected = "harness.rule.selected";
        public const string ActionDecoded = "harness.action.decoded";
        public const string ModuleOperationStarted = "harness.module.operation.started";
        public const string ModuleOperationApplied = "harness.module.operation.applied";
        public const string ModuleOperationCompleted = "harness.module.operation.completed";
        public const string LlmRequestSubmitted = "harness.llm.request.submitted";
        public const string LlmResponseReceived = "harness.llm.response.received";
        public const string LlmResponseInvalidJson = "harness.llm.response.invalid_json";
        public const string StepCompleted = "harness.step.completed";
        public const string StepTerminated = "harness.step.terminated";
        public const string Exception = "exception";
    }

    public static class Tags
    {
        public const string RunId = "run.id";
        public const string EpochIndex = "epoch.index";
        public const string StepIndex = "step.index";
        public const string RuleId = "rule.id";
        public const string RuleCandidateCount = "rule.candidate.count";
        public const string RuleSatisfiedCount = "rule.satisfied.count";
        public const string RuleSatisfiedIds = "rule.satisfied.ids";
        public const string RuleSelectionMode = "rule.selection.mode";
        public const string ConditionCount = "condition.count";
        public const string ModuleId = "module.id";
        public const string OperationCommand = "operation.command";
        public const string OperationCount = "operation.count";
        public const string StopReason = "stop.reason";
        public const string LlmPayloadType = "llm.payload.type";
        public const string LlmResponseLength = "llm.response.length";
        public const string LlmResponsePreview = "llm.response.preview";
        public const string ExceptionType = "exception.type";
        public const string ExceptionMessage = "exception.message";
        public const string ExceptionStacktrace = "exception.stacktrace";
        public const string ObservabilityBoundary = "observability.boundary";
    }

    public static Activity? StartActivity(
        string name,
        ActivityKind kind = ActivityKind.Internal,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return Source.StartActivity(name, kind, tags: tags);
    }

    public static void AddEvent(
        string name,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        AddEvent(Activity.Current, name, tags);
    }

    public static void AddEvent(
        Activity? activity,
        string name,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (activity is null)
        {
            return;
        }

        var eventTags = new ActivityTagsCollection();
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                eventTags[tag.Key] = tag.Value;
            }
        }

        activity.AddEvent(new ActivityEvent(name, tags: eventTags));
    }

    public static void RecordException(
        Activity? activity,
        Exception exception,
        string? boundary = null)
    {
        var tags = new ActivityTagsCollection
        {
            [Tags.ExceptionType] = exception.GetType().FullName ?? exception.GetType().Name,
            [Tags.ExceptionMessage] = exception.Message,
            [Tags.ExceptionStacktrace] = exception.ToString()
        };

        if (boundary is not null)
        {
            tags[Tags.ObservabilityBoundary] = boundary;
        }

        AddEvent(activity, Events.Exception, tags);
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    public static void RecordException(Exception exception, string? boundary = null)
    {
        RecordException(Activity.Current, exception, boundary);
    }

    public static void MarkTerminal(Activity? activity, string stopReason)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(Tags.StopReason, stopReason);
        AddEvent(
            activity,
            Events.StepTerminated,
            new[]
            {
                new KeyValuePair<string, object?>(Tags.StopReason, stopReason)
            });
    }
}
