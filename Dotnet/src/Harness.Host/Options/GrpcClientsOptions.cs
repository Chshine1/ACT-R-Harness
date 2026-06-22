using JetBrains.Annotations;

namespace Harness.Host.Options;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public class GrpcClientsOptions
{
    public const string Section = "GrpcClients";
    
    public required string DeclarativeMemoryAddress { get; init; }
    public required string FrostpunkWorldAddress { get; init; }
    public required string ProceduralMemoryAddress { get; init; }
    public required string NeuroCoreAddress { get; init; }
}