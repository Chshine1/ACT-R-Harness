using JetBrains.Annotations;

namespace Harness.Host.Options;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class HarnessOptions
{
    public const string Section = "Harness";

    public required bool Training { get; init; }
    public required int MaxEpochs { get; init; }
    public required int MaxStepsPerEpoch { get; init; }
    public required int StartupTimeoutSeconds { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string ArtifactRoot { get; init; }
    public required ScenarioOptions Scenario { get; init; }
}

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class ScenarioOptions
{
    public required string Name { get; init; }
    public required string GoalId { get; init; }
    public required string Query { get; init; }
    public required string GoalStatus { get; init; }
    public SeedMemoryChunkOptions? SeedMemoryChunk { get; init; }
}

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class SeedMemoryChunkOptions
{
    public required string Id { get; init; }
    public string? Module { get; init; }
    public string? Keywords { get; init; }
    public string? RelativeFilePath { get; init; }
}
