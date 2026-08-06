using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Harness.Shared.Observability;

public static class LoggingModel
{
    public static class Events
    {
        public const string RunStarted = TracingModel.Events.RunStarted;
        public const string RunCompleted = TracingModel.Events.RunCompleted;
        public const string EpochStarted = TracingModel.Events.EpochStarted;
        public const string EpochCompleted = TracingModel.Events.EpochCompleted;
        public const string StepStarted = TracingModel.Events.StepStarted;
        public const string StepBuffers = "harness.step.buffers";
        public const string StepTerminated = TracingModel.Events.StepTerminated;
        public const string StepSummary = "harness.step.summary";
        public const string ConditionsLoaded = TracingModel.Events.ConditionsLoaded;
        public const string ConditionsEvaluated = TracingModel.Events.ConditionsEvaluated;
        public const string ConditionsInvalidPayload = "harness.conditions.invalid_payload";
        public const string RulesLoaded = "harness.rules.loaded";
        public const string RuleSelected = TracingModel.Events.RuleSelected;
        public const string ActionDecoded = TracingModel.Events.ActionDecoded;
        public const string ModuleOperationStarted = TracingModel.Events.ModuleOperationStarted;
        public const string ModuleOperationCompleted = TracingModel.Events.ModuleOperationCompleted;
        public const string LlmRequestSubmitted = TracingModel.Events.LlmRequestSubmitted;
        public const string LlmResponseReceived = TracingModel.Events.LlmResponseReceived;
        public const string LlmResponseInvalidJson = TracingModel.Events.LlmResponseInvalidJson;
        public const string Exception = TracingModel.Events.Exception;
    }

    public static class Fields
    {
        public const string EventName = "event_name";
        public const string RunId = "run_id";
        public const string MaxEpochs = "max_epochs";
        public const string MaxStepsPerEpoch = "max_steps_per_epoch";
        public const string TraceId = "trace_id";
        public const string SpanId = "span_id";
        public const string Epoch = "epoch";
        public const string Step = "step";
        public const string RuleId = "rule_id";
        public const string RuleCandidateCount = "rule_candidate_count";
        public const string RuleSelectionMode = "rule_selection_mode";
        public const string SatisfiedRuleIds = "satisfied_rule_ids";
        public const string ConditionCount = "condition_count";
        public const string RuleCount = "rule_count";
        public const string Path = "path";
        public const string BufferCount = "buffer_count";
        public const string BufferSnapshot = "buffer_snapshot";
        public const string ModuleId = "module_id";
        public const string OperationCommand = "operation_command";
        public const string OperationCount = "operation_count";
        public const string Operations = "operations";
        public const string StopReason = "stop_reason";
        public const string Terminal = "terminal";
        public const string Success = "success";
        public const string Boundary = "boundary";
        public const string PayloadType = "payload_type";
        public const string ResponseLength = "response_length";
        public const string ResponsePreview = "response_preview";
        public const string ExceptionType = "exception_type";
        public const string ExceptionMessage = "exception_message";
        public const string ExceptionStacktrace = "exception_stacktrace";
    }

    public static void Log(
        ILogger logger,
        LogLevel level,
        string eventName,
        IEnumerable<KeyValuePair<string, object?>>? fields = null,
        Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        var state = StructuredLogState.Create(eventName, Activity.Current, fields, exception);
        logger.Log(
            level,
            new EventId(0, eventName),
            state,
            exception,
            static (value, _) => value.Message);
    }

    public static void LogException(
        ILogger logger,
        string boundary,
        Exception exception,
        IEnumerable<KeyValuePair<string, object?>>? fields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        ArgumentNullException.ThrowIfNull(exception);

        var allFields = new List<KeyValuePair<string, object?>>
        {
            new(Fields.Boundary, boundary)
        };

        if (fields is not null)
        {
            allFields.AddRange(fields);
        }

        Log(logger, LogLevel.Error, Events.Exception, allFields, exception);
    }

    private sealed class StructuredLogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly IReadOnlyList<KeyValuePair<string, object?>> _fields;

        private StructuredLogState(
            string message,
            IReadOnlyList<KeyValuePair<string, object?>> fields)
        {
            Message = message;
            _fields = fields;
        }

        public string Message { get; }

        public int Count => _fields.Count;

        public KeyValuePair<string, object?> this[int index] => _fields[index];

        public static StructuredLogState Create(
            string eventName,
            Activity? activity,
            IEnumerable<KeyValuePair<string, object?>>? fields,
            Exception? exception)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Fields.EventName] = eventName
            };

            AddActivityContext(values, activity);

            if (fields is not null)
            {
                foreach (var field in fields)
                {
                    values[field.Key] = field.Value;
                }
            }

            if (exception is not null)
            {
                values[Fields.ExceptionType] =
                    exception.GetType().FullName ?? exception.GetType().Name;
                values[Fields.ExceptionMessage] = exception.Message;
                values[Fields.ExceptionStacktrace] = exception.ToString();
            }

            return new StructuredLogState(
                eventName,
                values.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)).ToList());
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            _fields.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        private static void AddActivityContext(
            IDictionary<string, object?> values,
            Activity? activity)
        {
            if (activity is null)
            {
                return;
            }

            values[Fields.TraceId] = activity.TraceId.ToString();
            values[Fields.SpanId] = activity.SpanId.ToString();
            AddActivityTag(values, activity, TracingModel.Tags.RunId, Fields.RunId);
            AddActivityTag(values, activity, TracingModel.Tags.EpochIndex, Fields.Epoch);
            AddActivityTag(values, activity, TracingModel.Tags.StepIndex, Fields.Step);
            AddActivityTag(values, activity, TracingModel.Tags.RuleId, Fields.RuleId);
            AddActivityTag(values, activity, TracingModel.Tags.ModuleId, Fields.ModuleId);
            AddActivityTag(values, activity, TracingModel.Tags.OperationCommand, Fields.OperationCommand);
            AddActivityTag(values, activity, TracingModel.Tags.StopReason, Fields.StopReason);
        }

        private static void AddActivityTag(
            IDictionary<string, object?> values,
            Activity activity,
            string activityTag,
            string logField)
        {
            if (activity.GetTagItem(activityTag) is { } value)
            {
                values[logField] = value;
            }
        }
    }
}
