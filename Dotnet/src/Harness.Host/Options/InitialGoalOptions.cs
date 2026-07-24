using JetBrains.Annotations;

namespace Harness.Host.Options;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class InitialGoalOptions
{
    public required string Id { get; init; }
    public required string Query { get; init; }
}
