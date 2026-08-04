using JetBrains.Annotations;

namespace Harness.Core.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class DeclarativeMemoryOptions
{
    public const string Section = "DeclarativeMemory";

    public required double Decay { get; init; }
    public required double NoiseSd { get; init; }
    public required SeedOptions Seed { get; init; }

    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
    public class SeedOptions
    {
        public required string WorkspaceRoot { get; init; }
        public SeedMemoryChunkOptions? SeedMemoryChunk { get; init; }

        [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
        public class SeedMemoryChunkOptions
        {
            public required string Id { get; init; }
            public string? Module { get; init; }
            public string? Keywords { get; init; }
            public string? RelativeFilePath { get; init; }
        }
    }
}