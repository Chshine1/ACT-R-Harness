using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

public class DeltaDetectorModule : ModuleBase
{
    public override string ModuleId => "delta_detector";

    private DetectorStatus _status = DetectorStatus.Idle;
    private List<FileSnapshot>? _currentPerception;
    private List<FileSnapshot>? _referencePerception;
    private List<ChangeEvent>? _comparisonResult;

    public override BufferState GetBufferState()
    {
        var data = new Struct
        {
            Fields =
            {
                ["status"] = Value.ForString(_status.ToString().ToLowerInvariant()),
            }
        };

        if (_currentPerception is not null)
        {
            data.Fields["current_perception"] = Value.ForList(
                _currentPerception.Select(s => Value.ForStruct(s.ToStruct())).ToArray()
            );
        }
        else
        {
            data.Fields["current_perception"] = Value.ForNull();
        }

        if (_referencePerception is not null)
        {
            data.Fields["reference_perception"] = Value.ForList(
                _referencePerception.Select(s => Value.ForStruct(s.ToStruct())).ToArray()
            );
        }
        else
        {
            data.Fields["reference_perception"] = Value.ForNull();
        }

        if (_comparisonResult is { Count: > 0 })
        {
            data.Fields["comparison_result"] = Value.ForList(
                _comparisonResult.Select(c => Value.ForStruct(c.ToStruct())).ToArray()
            );
        }
        else
        {
            data.Fields["comparison_result"] = Value.ForNull();
        }

        return new BufferState { ModuleId = ModuleId, Data = data };
    }

    [ModuleCommand("compare")]
    private void Compare()
    {
        if (_status != DetectorStatus.Idle) return;
        if (_currentPerception is null || _referencePerception is null) return;

        _status = DetectorStatus.Comparing;

        var changes = ComputeDifferences(_referencePerception, _currentPerception);
        _comparisonResult = changes;
        _status = DetectorStatus.Done;
    }

    private static List<ChangeEvent> ComputeDifferences(
        List<FileSnapshot> oldSnapshots,
        List<FileSnapshot> newSnapshots)
    {
        var changes = new List<ChangeEvent>();
        var oldMap = oldSnapshots.ToDictionary(s => s.FilePath);
        var newMap = newSnapshots.ToDictionary(s => s.FilePath);

        foreach (var (path, newSnap) in newMap)
        {
            if (!oldMap.TryGetValue(path, out var oldSnap))
            {
                changes.Add(new ChangeEvent(ChangeType.Added, path, newSnap.ExtractedStructure));
            }
            else if (oldSnap.ContentHash != newSnap.ContentHash)
            {
                var diffDetail = ComputeStructuralDiff(oldSnap.ExtractedStructure, newSnap.ExtractedStructure);
                changes.Add(new ChangeEvent(ChangeType.Modified, path, diffDetail));
            }
        }

        changes.AddRange(oldMap.Keys.Except(newMap.Keys).Select(path => new ChangeEvent(ChangeType.Deprecated, path, new Struct())));

        return changes;
    }

    private static Struct ComputeStructuralDiff(Struct old, Struct @new) => throw new NotImplementedException();
}