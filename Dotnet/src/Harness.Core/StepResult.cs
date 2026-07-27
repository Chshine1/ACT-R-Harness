namespace Harness.Core;

public sealed record ModuleSnapshot(string ModuleId, IReadOnlyDictionary<string, object?> Data);

public sealed record OperationTrace(string TargetModuleId, string Command, IReadOnlyDictionary<string, object?> Params);

public sealed record StepResult(
    IReadOnlyList<ModuleSnapshot> BufferStatesBefore,
    IReadOnlyList<ModuleSnapshot> BufferStatesAfter,
    IReadOnlyList<string> SatisfiedRuleIds,
    string? SelectedRuleId,
    IReadOnlyList<OperationTrace> Operations,
    bool IsTerminal,
    string StopReason,
    string? ErrorMessage = null
);
