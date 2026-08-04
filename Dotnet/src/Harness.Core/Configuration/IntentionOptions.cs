using JetBrains.Annotations;

namespace Harness.Core.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class IntentionOptions
{
    public const string Section = "Intention";
    
    public required SeedOptions Seed { get; init; }

    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
    public class SeedOptions
    {
        public required string Name { get; init; }
        public required string GoalId { get; init; }
        public required string Query { get; init; }
        public required string GoalStatus { get; init; }
    }
}