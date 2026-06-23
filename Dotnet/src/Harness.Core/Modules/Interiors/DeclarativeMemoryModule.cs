using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core.Modules;

public partial class DeclarativeMemoryModule
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
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        ArgumentNullException.ThrowIfNull(clock);
        clock.OnTickAsync += OnTickAsync;
    }

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
            chunk.Slots.Add(slot.Key, slot.Value.StringValue);

        _client.AddChunk(new AddChunkRequest { Chunk = chunk });
    }

    private void Retrieve(Struct parameters)
    {
        var request = new RetrieveRequest();
        foreach (var field in parameters.Fields) request.Cue.Add(field.Key, field.Value.StringValue);

        var response = _client.Retrieve(request);
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
}