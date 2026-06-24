using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Core.Modules;

public partial class DeclarativeMemoryModule : IModule
{
    public string ModuleId => "DeclarativeMemory";

    public BufferState GetBufferState()
    {
        var data = new Struct();
        if (_lastRetrieved != null)
        {
            var chunkStruct = new Struct
            {
                Fields =
                {
                    ["id"] = Value.ForString(_lastRetrieved.Id),
                    ["creation_time"] = Value.ForNumber(_lastRetrieved.CreationTime),
                    ["slots"] = Value.ForStruct(new Struct())
                }
            };
            var slotsStruct = new Struct();
            foreach (var slot in _lastRetrieved.Slots)
                slotsStruct.Fields[slot.Key] = Value.ForString(slot.Value);
            chunkStruct.Fields["slots"] = Value.ForStruct(slotsStruct);

            data.Fields["retrieved_chunk"] = Value.ForStruct(chunkStruct);
        }
        else
        {
            data.Fields["retrieved_chunk"] = Value.ForNull();
        }

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
                ["AddChunk"] =
                    """
                    {
                        "type": "object",
                        "properties": {
                            "id": { "type": "string" },
                            "creation_time": { "type": "number" },
                            "slots": { "type": "object", "additionalProperties": { "type": "string" } }
                        },
                        "required": ["id", "creation_time", "slots"]
                    }
                    """,
                ["Retrieve"] =
                    """
                    {
                        "type": "object",
                        "additionalProperties": { "type": "string" }
                    }
                    """
            }
        };
    }
}