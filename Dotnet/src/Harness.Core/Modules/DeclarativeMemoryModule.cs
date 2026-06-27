using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Harness.Abstractions.Modules;

namespace Harness.Core.Modules;

[ModuleCommandRequest(
    """
    {
        "type": "object",
        "additionalProperties": { "type": "string" }
    }
    """)]
public record RetrieveChunkRequest(Struct Cue) : IStructRepresentable<RetrieveChunkRequest>
{
    public Struct ToStruct() => Cue;
    public static RetrieveChunkRequest FromStruct(Struct s) => new(s);
}

[ModuleCommandRequest(
    """
    {
        "id": { "type": "string" },
        "slots": {
            "type": "object",
            "additionalProperties": { "type": "string" }
        }
    }
    """)]
public record AddChunkRequest(string Id, Struct Slots) : IStructRepresentable<AddChunkRequest>
{
    public Struct ToStruct() => new() { Fields = { ["id"] = Value.ForString(Id), ["slots"] = Value.ForStruct(Slots) } };

    public static AddChunkRequest FromStruct(Struct s) =>
        new(s.Fields["id"].StringValue, s.Fields["slots"].StructValue);
}

public class DeclarativeMemoryModule : ModuleBase
{
    private const double DefaultDeltaTimeSeconds = 60.0;
    private readonly DeclarativeMemory.DeclarativeMemoryClient _client;
    private readonly IModuleRegistry _moduleRegistry;
    private MemoryChunk? _lastRetrieved;

    public DeclarativeMemoryModule(
        DeclarativeMemory.DeclarativeMemoryClient client,
        IClock clock,
        IModuleRegistry moduleRegistry)
    {
        _client = client;
        _moduleRegistry = moduleRegistry;
        clock.OnTickAsync += OnTickAsync;
    }

    public override string ModuleId => "declarative_memory";

    public override BufferState GetBufferState()
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

    [ModuleCommand("add_chunk")]
    protected void AddChunk(AddChunkRequest request)
    {
        var chunk = new MemoryChunk
        {
            Id = request.Id,
            CreationTime = Now()
        };
        foreach (var slot in request.Slots.Fields)
            chunk.Slots.Add(slot.Key, slot.Value.StringValue);

        _client.AddChunk(new Harness.Abstractions.Actr.Services.AddChunkRequest { Chunk = chunk });
    }

    [ModuleCommand("retrieve_chunk")]
    protected void RetrieveChunk(RetrieveChunkRequest request)
    {
        var rpcRequest = new RetrieveRequest();
        foreach (var field in request.Cue.Fields)
        {
            rpcRequest.Cue.Add(field.Key, field.Value.StringValue);
        }

        var response = _client.Retrieve(rpcRequest);
        _lastRetrieved = response.Chunk;
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

    private static double Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}