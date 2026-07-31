using JetBrains.Annotations;

namespace Harness.Core.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class DeclarativeMemoryOptions
{
    public const string Section = "DeclarativeMemory";
    
    public required double Decay { get; init; }
    public required double NoiseSd { get; init; }
}
