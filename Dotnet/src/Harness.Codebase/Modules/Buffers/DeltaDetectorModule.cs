using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Codebase.Modules;

public partial class DeltaDetectorModule : ModuleBase
{
    public override string ModuleId => "delta_detector";

    public override BufferState GetBufferState()
    {
        var data = new Struct
        {
            Fields =
            {
                ["status"] = Value.ForString(_status.ToString().ToLowerInvariant())
            }
        };

        // The raw perception to compare against memory.
        if (_currentPerception != null)
        {
            var perceptionList = _currentPerception.Select(s => Value.ForStruct(s.ToStruct())).ToArray();
            data.Fields["current_perception"] = Value.ForList(perceptionList);
        }
        else
        {
            data.Fields["current_perception"] = Value.ForNull();
        }

        // Differences found after a comparison.
        if (_comparisonResult is { Count: > 0 })
        {
            var changeList = _comparisonResult.Select(c => Value.ForStruct(c.ToStruct())).ToArray();
            data.Fields["comparison_result"] = Value.ForList(changeList);
        }
        else
        {
            data.Fields["comparison_result"] = Value.ForNull();
        }

        return new BufferState
        {
            ModuleId = ModuleId,
            Data = data
        };
    }
}