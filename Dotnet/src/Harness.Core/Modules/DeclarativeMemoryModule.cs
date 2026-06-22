using Actr;
using Actr.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Harness.Core.Modules;

public class DeclarativeMemoryModule(DeclarativeMemory.DeclarativeMemoryClient client)
    : IModule
{
    private MemoryChunk? _lastRetrieved;

    public string ModuleId => "DeclarativeMemory";

    public void OperateBuffer(BufferOperation op)
    {
        try
        {
            switch (op.Command)
            {
                case "addChunk":
                    AddChunk(op.Params);
                    break;
                case "retrieve":
                    Retrieve(op.Params);
                    break;
                case "updateActivation":
                    UpdateActivation(op.Params);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"DeclarativeMemory does not support command '{op.Command}'.");
            }
        }
        catch (RpcException e)
        {
            throw new InvalidOperationException(
                $"Declarative memory RPC failed: {e.Status.Detail}", e);
        }
    }

    private void AddChunk(Struct parameters)
    {
        var chunk = new MemoryChunk
        {
            Id = parameters.Fields["id"].StringValue,
            CreationTime = parameters.Fields["creation_time"].NumberValue
        };
        foreach (var slot in parameters.Fields["slots"].StructValue.Fields)
        {
            chunk.Slots.Add(slot.Key, slot.Value.StringValue);
        }

        client.AddChunk(new AddChunkRequest { Chunk = chunk });
    }

    private void Retrieve(Struct parameters)
    {
        var request = new RetrieveRequest();
        foreach (var field in parameters.Fields)
        {
            request.Cue.Add(field.Key, field.Value.StringValue);
        }

        var response = client.Retrieve(request);
        _lastRetrieved = response.Chunk;
    }

    private void UpdateActivation(Struct parameters)
    {
        var request = new UpdateActivationRequest
        {
            ChunkId = parameters.Fields["chunk_id"].StringValue,
            Delta = parameters.Fields["delta"].NumberValue
        };
        client.UpdateActivation(request);
    }

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
                    ["slots"] = Value.ForStruct(
                        new Struct())
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
        var schema = new ModuleSchema { ModuleId = ModuleId };
        schema.CommandSchemas.Add(
            "addChunk",
            """
            {
                "id": "string",
                "creation_time": "number",
                "slots": { "type": "object", "additionalProperties": "string" }
            }
            """
        );
        schema.CommandSchemas.Add(
            "retrieve",
            """
            {
                "type": "object",
                "additionalProperties": "string"
            }
            """
        );
        schema.CommandSchemas.Add(
            "updateActivation",
            """
            {
                "chunk_id": "string",
                "delta": "number"
            }
            """
        );
        return schema;
    }
}