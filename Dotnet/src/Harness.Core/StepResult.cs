namespace Harness.Core;

public sealed record ModuleSnapshot(string ModuleId, IReadOnlyDictionary<string, object?> Data);

public sealed record OperationTrace(string TargetModuleId, string Command, IReadOnlyDictionary<string, object?> Params);

public sealed record BufferFieldChange(string Path, object? Before, object? After);

public sealed record BufferStateChange(string ModuleId, IReadOnlyList<BufferFieldChange> Changes);

public sealed record StepResult(
    IReadOnlyList<ModuleSnapshot> BufferStatesBefore,
    IReadOnlyList<ModuleSnapshot> BufferStatesAfter,
    IReadOnlyList<string> SatisfiedRuleIds,
    string? SelectedRuleId,
    IReadOnlyList<OperationTrace> Operations,
    IReadOnlyList<BufferStateChange> BufferChanges,
    bool IsTerminal,
    string StopReason,
    string? FailureStage = null,
    string? ErrorType = null,
    string? ErrorMessage = null,
    string? ErrorDetails = null
);
