using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core.Modules;

public class DeclarativeMemoryModule : IModule
{
    private readonly DeclarativeMemory.DeclarativeMemoryClient _client;
    private readonly IModuleRegistry _moduleRegistry;
    private MemoryChunk? _lastRetrieved;
    private const double DefaultDeltaTimeSeconds = 60.0;

    public DeclarativeMemoryModule(
        DeclarativeMemory.DeclarativeMemoryClient client,
        IClock clock,
        IModuleRegistry moduleRegistry)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        ArgumentNullException.ThrowIfNull(clock);
        clock.OnTickAsync += OnTickAsync;
    }

    public string ModuleId => "DeclarativeMemory";

    public void OperateBuffer(BufferOperation op)
    {
        try
        {
            switch (op.Command)
            {
                case "AddChunk":
                    AddChunk(op.Params);
                    break;
                case "Retrieve":
                    Retrieve(op.Params);
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

        _client.AddChunk(new AddChunkRequest { Chunk = chunk });
    }

    private void Retrieve(Struct parameters)
    {
        var request = new RetrieveRequest();
        foreach (var field in parameters.Fields)
        {
            request.Cue.Add(field.Key, field.Value.StringValue);
        }

        var response = _client.Retrieve(request);
        _lastRetrieved = response.Chunk;
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
        var schema = new ModuleSchema { ModuleId = ModuleId };
        schema.CommandSchemas.Add(
            "AddChunk",
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
            """
        );
        schema.CommandSchemas.Add(
            "Retrieve",
            """
            {
                "type": "object",
                "additionalProperties": { "type": "string" }
            }
            """
        );
        return schema;
    }

    private async Task OnTickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        var snapshots = new Struct();
        foreach (var module in _moduleRegistry.GetModules())
        {
            var state = module.GetBufferState();
            snapshots.Fields[state.ModuleId] = Value.ForStruct(state.Data);
        }

        var request = new TickMemoryRequest
        {
            DeltaTime = DefaultDeltaTimeSeconds,
            BufferSnapshots = snapshots
        };

        await _client.TickMemoryAsync(request, cancellationToken: cancellationToken);
    }
}