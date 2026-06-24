using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Harness.Abstractions.Reward;

namespace Harness.Core.Modules;

public partial class PerceptionMotorModule : IModule, IRewardStateProvider
{
    public string ModuleId => "PerceptionMotor";

    public BufferState GetBufferState()
    {
        var state = _home.GetCurrentState();
        var data = new Struct();

        var roomsList = state.Rooms
            .Select(room => Value.ForStruct(
                new Struct
                {
                    Fields =
                    {
                        ["temperature"] = Value.ForNumber(room.Temperature),
                        ["humidity"] = Value.ForNumber(room.Humidity),
                        ["air_quality"] = Value.ForNumber(room.AirQuality)
                    }
                }
            ))
            .ToArray();

        data.Fields["rooms"] = Value.ForList(roomsList);
        data.Fields["total_energy"] = Value.ForNumber(state.TotalEnergy);

        return new BufferState
        {
            ModuleId = ModuleId,
            Data = data
        };
    }

    public ModuleSchema GetOperationSchema()
    {
        return new ModuleSchema
        {
            ModuleId = ModuleId,
            CommandSchemas =
            {
                ["SetLight"] =
                    """
                    {
                        "type": "object",
                        "properties": {
                            "roomIndex": { "type": "integer" },
                            "on": { "type": "boolean" }
                        },
                        "required": ["roomIndex", "on"]
                    }
                    """,
                ["SetHVAC"] =
                    """
                    {
                        "type": "object",
                        "properties": {
                            "roomIndex": { "type": "integer" },
                            "targetTemp": { "type": "number" }
                        },
                        "required": ["roomIndex", "targetTemp"]
                    }
                    """,
                ["SetAppliance"] =
                    """
                    {
                        "type": "object",
                        "properties": {
                            "roomIndex": { "type": "integer" },
                            "on": { "type": "boolean" }
                        },
                        "required": ["roomIndex", "on"]
                    }
                    """
            }
        };
    }
}