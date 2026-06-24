using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Modules;
using Enum = System.Enum;

namespace Harness.Codebase;

public enum ScanStatus
{
    Idle,
    Scanning,
    Complete,
    Error
}

public enum DetectorStatus
{
    Idle,
    Comparing,
    Done
}

public sealed record FileSnapshot(
    string FilePath,
    string ContentHash,
    Struct ExtractedStructure
) : IStructRepresentable<FileSnapshot>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["file_path"] = Value.ForString(FilePath),
            ["content_hash"] = Value.ForString(ContentHash),
            ["structure"] = Value.ForStruct(ExtractedStructure)
        }
    };

    public static FileSnapshot FromStruct(Struct s)
    {
        return new FileSnapshot(
            s.Fields["file_path"].StringValue,
            s.Fields["content_hash"].StringValue,
            s.Fields["structure"].StructValue
        );
    }
}

public enum ChangeType
{
    Added,
    Modified,
    Deprecated
}

public sealed record ChangeEvent(
    ChangeType Type,
    string ChunkIdentifier,
    Struct Details
) : IStructRepresentable<ChangeEvent>
{
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["type"] = Value.ForString(Type.ToString().ToLowerInvariant()),
            ["chunk_id"] = Value.ForString(ChunkIdentifier),
            ["details"] = Value.ForStruct(Details)
        }
    };

    public static ChangeEvent FromStruct(Struct s)
    {
        return new ChangeEvent(
            Enum.Parse<ChangeType>(s.Fields["type"].StringValue, ignoreCase: true),
            s.Fields["chunk_id"].StringValue,
            s.Fields["details"].StructValue
        );
    }
}