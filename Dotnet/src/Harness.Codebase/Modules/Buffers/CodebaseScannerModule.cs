using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

public partial class CodebaseScannerModule : ModuleBase
{
    public override string ModuleId => "codebase_scanner";

    public override BufferState GetBufferState()
    {
        var data = new Struct
        {
            Fields =
            {
                // Reflect the current scan phase
                ["status"] = Value.ForString(_status.ToString().ToLowerInvariant()),
                ["last_scan_time"] = _lastScanTime != null
                    ? Value.ForString(_lastScanTime.Value.ToString("O"))
                    : Value.ForNull()
            }
        };

        // The latest perceptual snapshot is a list of file snapshots.
        // It is only present when status is Complete; otherwise null.
        if (_status == ScanStatus.Complete && _latestSnapshots != null)
        {
            data.Fields["file_snapshots"] = Value.ForList(
                _latestSnapshots.Select(s => Value.ForStruct(s.ToStruct())).ToArray()
            );
        }
        else
        {
            data.Fields["file_snapshots"] = Value.ForNull();
        }

        // The set of paths that will be scanned next time a scan_files command is issued.
        // Populated by goal or higher-level logic.
        data.Fields["target_paths"] = _targetPaths != null
            ? Value.ForList(_targetPaths.Select<string, Value>(Value.ForString).ToArray())
            : Value.ForNull();

        return new BufferState
        {
            ModuleId = ModuleId,
            Data = data
        };
    }
}