using JetBrains.Annotations;

namespace Harness.Core.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class ProceduralMemoryOptions
{
    public const string Section = "ProceduralMemory";
    
    public required double Temperature { get; init; }
    public required double LearningRate { get; init; }
    public required string RulesPath { get; init; }
    public required double DefaultUtility { get; init; }
    public required int RandomSeed { get; init; }
}
