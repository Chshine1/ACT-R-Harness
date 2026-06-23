using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Type = System.Type;

namespace Harness.Core.Modules;

public partial class PerceptionMotorModule
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

    public void OperateBuffer(BufferOperation operation)
    {
        _pendingOperations.Add(operation);
    }

    // IRewardStateProvider
    public Type StateType => typeof(PerceptionRewardState);

    public Task<object> GetRewardStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<object>(_home.GetCurrentState());
    }

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
            dict[field.Key] = ConvertValue(field.Value) ?? throw new ArgumentNullException(nameof(structValue));

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