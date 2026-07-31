using JetBrains.Annotations;

namespace Harness.Core.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class LlmClientOptions
{
    public const string Section = "LlmClient";
    
    public required string Model { get; init; }
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }
}
