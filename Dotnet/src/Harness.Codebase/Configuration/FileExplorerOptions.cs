using JetBrains.Annotations;

namespace Harness.Codebase.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class FileExplorerOptions
{
    public const string Section = "FileExplorer";
    
    public required int TopK { get; init; }
    public required SeedOptions Seed { get; init; }

    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
    public class SeedOptions
    {
        public required string WorkspaceRoot { get; init; }
    }
}