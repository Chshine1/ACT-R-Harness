using JetBrains.Annotations;

namespace Harness.Host.Options;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class HarnessOptions
{
    public const string Section = "Harness";
    
    public required bool Training { get; init; }
    public required int MaxEpochs { get; init; }
    public required int MaxStepsPerEpoch { get; init; }
}