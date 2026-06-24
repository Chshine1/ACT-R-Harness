using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

public enum ScanStatus
{
    Idle,
    Scanning,
    Complete,
    Error
}

public sealed record FileSnapshot(string FilePath, string ContentHash, Struct ExtractedStructure)
{
    public Struct ToStruct()
    {
        return new Struct
        {
            Fields =
            {
                ["file_path"] = Value.ForString(FilePath),
                ["content_hash"] = Value.ForString(ContentHash),
                ["extracted_structure"] = Value.ForStruct(ExtractedStructure)
            }
        };
    }
}

[ModuleCommandRequest(
    """
    {
        "paths": {
            "type": "array",
            "items": { "type": "string" }
        }
    }
    """
)]
public record ScanFilesRequest(IEnumerable<string> Paths) : IStructRepresentable<ScanFilesRequest>
{
    public static ScanFilesRequest FromStruct(Struct value)
    {
        var list = value.Fields["paths"].ListValue.Values;
        var paths = list.Select(v => v.StringValue);
        return new ScanFilesRequest(paths);
    }
}

public partial class CodebaseScannerModule
{
    private ScanStatus _status = ScanStatus.Idle;
    private DateTime? _lastScanTime;
    private List<FileSnapshot>? _latestSnapshots;
    private List<string>? _targetPaths;

    [ModuleCommand("scan_files")]
    public void ScanFiles(ScanFilesRequest request)
    {
    }

    [ModuleCommand("abort")]
    public void Abort()
    {
    }

    [ModuleCommand("get_status")]
    public void GetStatus()
    {
    }
}