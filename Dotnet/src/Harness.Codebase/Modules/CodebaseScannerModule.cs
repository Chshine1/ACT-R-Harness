using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

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
    public Struct ToStruct() => new()
    {
        Fields =
        {
            ["paths"] = Value.ForList(Paths.Select(Value.ForString).ToArray())
        }
    };

    public static ScanFilesRequest FromStruct(Struct s)
    {
        var list = s.Fields["paths"].ListValue.Values;
        return new ScanFilesRequest(list.Select(v => v.StringValue));
    }
}

public class CodebaseScannerModule : ModuleBase
{
    public override string ModuleId => "codebase_scanner";

    private ScanStatus _status = ScanStatus.Idle;
    private DateTime? _lastScanTime;
    private List<FileSnapshot>? _latestSnapshots;
    private List<string>? _targetPaths;

    public override BufferState GetBufferState()
    {
        var data = new Struct
        {
            Fields =
            {
                ["status"]         = Value.ForString(_status.ToString().ToLowerInvariant()),
                ["last_scan_time"] = _lastScanTime is not null
                    ? Value.ForString(_lastScanTime.Value.ToString("O"))
                    : Value.ForNull(),
            }
        };

        if (_status == ScanStatus.Complete && _latestSnapshots is not null)
        {
            data.Fields["file_snapshots"] = Value.ForList(
                _latestSnapshots.Select(s => Value.ForStruct(s.ToStruct())).ToArray()
            );
        }
        else
        {
            data.Fields["file_snapshots"] = Value.ForNull();
        }

        data.Fields["target_paths"] = _targetPaths is not null
            ? Value.ForList(_targetPaths.Select(Value.ForString).ToArray())
            : Value.ForNull();

        return new BufferState { ModuleId = ModuleId, Data = data };
    }

    [ModuleCommand("scan_files")]
    protected void ScanFiles(ScanFilesRequest request)
    {
        if (_status != ScanStatus.Idle) return;

        _targetPaths = request.Paths.ToList();
        _status = ScanStatus.Scanning;

        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        try
        {
            var snapshots = new List<FileSnapshot>();
            foreach (var path in _targetPaths ?? [])
            {
                var content = await File.ReadAllTextAsync(path);
                var hash = ComputeSha256(content);
                var structure = ExtractRelevantStructure(path, content);
                snapshots.Add(new FileSnapshot(path, hash, structure));
            }

            _latestSnapshots = snapshots;
            _lastScanTime = DateTime.UtcNow;
            _status = ScanStatus.Complete;
        }
        catch
        {
            _status = ScanStatus.Error;
        }
    }

    private static string ComputeSha256(string content) => throw new NotImplementedException();
    private static Struct ExtractRelevantStructure(string path, string content) => throw new NotImplementedException();
}