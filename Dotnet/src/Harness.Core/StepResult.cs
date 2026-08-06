namespace Harness.Core;

public sealed record OperationTrace(string TargetModuleId, string Command, IReadOnlyDictionary<string, object?> Params);

public sealed record StepResult(
    bool IsTerminal,
    string StopReason,
    string? SelectedRuleId = null,
    IReadOnlyList<OperationTrace>? Operations = null
);
