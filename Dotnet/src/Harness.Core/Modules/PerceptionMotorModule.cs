using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Type = System.Type;

namespace Harness.Core.Modules;
public class PerceptionMotorModule : IModule, IRewardStateProvider
{
    private readonly IClock _clock;
    private readonly SimulatedHome _home;
    private readonly List<BufferOperation> _pendingOperations = [];

    public PerceptionMotorModule(IClock clock, SimulatedHome? home = null)
    {
        _clock = clock;
        _home = home ?? new SimulatedHome();
        _clock.OnTickAsync += OnTickAsync;
    }

    public string ModuleId => "PerceptionMotor";

    public BufferState GetBufferState()
    {
        return new BufferState
        {
            ModuleId = ModuleId,
            Data = Struct.Parser.ParseJson(JsonSerializer.Serialize(_home.GetCurrentState()))
        };
    }

    public ModuleSchema GetOperationSchema()
    {
        var schema = new ModuleSchema { ModuleId = ModuleId };

        schema.CommandSchemas.Add(
            "SetLight",
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
        );

        schema.CommandSchemas.Add(
            "SetHVAC",
            """
            {
                "type": "object",
                "properties": {
                    "roomIndex": { "type": "integer" },
                    "targetTemp": { "type": "number" }
                },
                "required": ["roomIndex", "targetTemp"]
            }
            """
        );

        schema.CommandSchemas.Add(
            "SetAppliance",
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
        );

        return schema;
    }

    public void OperateBuffer(BufferOperation operation)
    {
        _pendingOperations.Add(operation);
    }

    // IRewardStateProvider
    public Type StateType => typeof(PerceptionRewardState);

    public Task<object> GetRewardStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<object>(_home.GetCurrentState());

    private Task OnTickAsync(StepState stepState, CancellationToken cancellationToken)
    {
        foreach (var op in _pendingOperations)
        {
            var parameters = StructToDictionary(op.Params);
            _home.ApplyAction(op.Command, parameters);
        }
        _pendingOperations.Clear();

        _home.Update(SimulatedHome.TimeStepHours);

        return Task.CompletedTask;
    }

    private static Dictionary<string, object> StructToDictionary(Struct? structValue)
    {
        var dict = new Dictionary<string, object>();
        if (structValue == null) return dict;

        foreach (var field in structValue.Fields)
        {
            dict[field.Key] = ConvertValue(field.Value) ?? throw new ArgumentNullException(nameof(structValue));
        }
        return dict;
    }

    private static object? ConvertValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.NullValue => null,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StructValue => StructToDictionary(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values.Select(ConvertValue).ToList(),
            _ => null
        };
    }
}