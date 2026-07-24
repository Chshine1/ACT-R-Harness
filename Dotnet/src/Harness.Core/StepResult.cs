using Harness.Abstractions.Actr;

namespace Harness.Core;

public enum StepStopReason
{
    Continue,
    NoApplicableRule,
    GoalReachedTerminalStatus
}

public sealed record StepResult(
    StepStopReason StopReason,
    IReadOnlyList<string> SatisfiedRuleIds,
    string? SelectedRuleId,
    IReadOnlyList<BufferOperation> AppliedOperations)
{
    public bool IsTerminal => StopReason != StepStopReason.Continue;
}
