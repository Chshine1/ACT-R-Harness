using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

public enum DetectorStatus
{
    Idle,
    Comparing,
    Done
}

public sealed record ChangeEvent(string ChangeType, string ChunkIdentifier, Struct Details)
{
    public Struct ToStruct()
    {
        return new Struct
        {
            Fields =
            {
                ["change_type"] = Value.ForString(ChangeType),
                ["chunk_identifier"] = Value.ForString(ChunkIdentifier),
                ["details"] = Value.ForStruct(Details)
            }
        };
    }
}

[ModuleCommandRequest(
    """
    {
        "perception_id": "string"
    }
    """
)]
public record CompareWithMemoryRequest(string PerceptionId) : IStructRepresentable<CompareWithMemoryRequest>
{
    public static CompareWithMemoryRequest FromStruct(Struct value)
    {
        return new CompareWithMemoryRequest(value.Fields["perception_id"].StringValue);
    }
}

public partial class DeltaDetectorModule
{
    private DetectorStatus _status = DetectorStatus.Idle;
    private List<FileSnapshot>? _currentPerception;
    private List<ChangeEvent>? _comparisonResult;

    [ModuleCommand("compare_with_memory")]
    public void CompareWithMemory(CompareWithMemoryRequest request)
    {
    }

    [ModuleCommand("get_changes")]
    public void GetChanges()
    {
    }
}