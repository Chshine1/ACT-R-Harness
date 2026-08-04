using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Harness.Abstractions.Modules;
using Harness.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Harness.Core.Modules;

[ModuleCommandRequest(
    """
    {
        "cue": {
            "type": "object",
            "additionalProperties": { "type": "string" }
        }
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

public class DeclarativeMemoryModule : ModuleBase, ITrainingLifecycle
{
    private const double DefaultDeltaTimeSeconds = 60.0;
    private readonly DeclarativeMemoryService _memoryService;
    private readonly DeclarativeMemoryOptions _options;
    private readonly IReadOnlyCollection<IModule> _modules;
    private MemoryChunk? _lastRetrieved;
    private readonly HashSet<string> _knownSlotKeys = [];

    public DeclarativeMemoryModule(
        DeclarativeMemoryService memoryService,
        IOptions<DeclarativeMemoryOptions> options,
        IEnumerable<IModule> modules,
        IClock clock)
    {
        _memoryService = memoryService;
        _options = options.Value;
        _modules = modules.ToHashSet();
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

        data.Fields["available_slot_keys"] = Value.ForList(_knownSlotKeys.Select(Value.ForString).ToArray());

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
        {
            chunk.Slots.Add(slot.Key, slot.Value.StringValue);
            _knownSlotKeys.Add(slot.Key);
        }

        _memoryService.AddChunk(new Harness.Abstractions.Actr.Services.AddChunkRequest { Chunk = chunk });
    }

    [ModuleCommand("retrieve_chunk")]
    protected void RetrieveChunk(RetrieveChunkRequest request)
    {
        var rpcRequest = new RetrieveRequest();
        foreach (var field in request.Cue.Fields)
        {
            rpcRequest.Cue.Add(field.Key, field.Value.StringValue);
        }

        var response = _memoryService.Retrieve(rpcRequest);
        _lastRetrieved = response.Chunk;
    }

    private async Task OnTickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        var snapshots = new Struct();
        foreach (var module in _modules)
        {
            var state = module.GetBufferState();
            snapshots.Fields[state.ModuleId] = Value.ForStruct(state.Data);
        }

        var request = new TickMemoryRequest
        {
            DeltaTime = DefaultDeltaTimeSeconds,
            BufferSnapshots = snapshots
        };

        await _memoryService.TickMemoryAsync(request, cancellationToken);
    }

    private static double Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    public Task OnEpochStartedAsync(EpochContext context)
    {
        var workspaceRoot = Path.GetFullPath(_options.Seed.WorkspaceRoot);
        if (!Directory.Exists(workspaceRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{workspaceRoot}' does not exist.");
        }

        if (_options.Seed.SeedMemoryChunk is not { } chunk || string.IsNullOrWhiteSpace(chunk.Id))
            return Task.CompletedTask;

        var slotFields = new Struct
        {
            Fields =
            {
                ["module"] = Value.ForString(chunk.Module),
                ["keywords"] = Value.ForString(chunk.Keywords),
            }
        };

        if (!string.IsNullOrWhiteSpace(chunk.RelativeFilePath))
        {
            var absolutePath = Path.GetFullPath(Path.Combine(workspaceRoot, chunk.RelativeFilePath));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"Seed file '{absolutePath}' does not exist.",
                    absolutePath);
            }

            slotFields.Fields["file"] = Value.ForString(absolutePath);
        }

        AddChunk(new AddChunkRequest(Id: chunk.Id, Slots: slotFields));

        return Task.CompletedTask;
    }
}