using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Type = System.Type;

namespace Harness.Abstractions.Modules;

public abstract class ModuleBase : IModule, IProvideLogger
{
    private record struct CommandCache(Action<IModule, Struct?> Action, string Schema);

    private static readonly ConcurrentDictionary<Type, Dictionary<string, CommandCache>> Cache = new();
    private readonly Dictionary<string, CommandCache> _commandMap;

    protected ModuleBase(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
        var type = GetType();
        _commandMap = Cache.GetOrAdd(type, static t => BuildCommandMap(t));
    }

    public ILogger Logger { get; }

    private static Dictionary<string, CommandCache> BuildCommandMap(Type moduleType)
    {
        var map = new Dictionary<string, CommandCache>();
        var methods = moduleType.GetMethods(BindingFlags.Instance);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<ModuleCommandAttribute>();
            if (attr is null) continue;

            var parameters = method.GetParameters();

            var moduleParam = Expression.Parameter(typeof(IModule));
            var structParam = Expression.Parameter(typeof(Struct));

            Expression call;
            string schema;
            switch (parameters.Length)
            {
                case > 1 or < 0:
                    throw new InvalidOperationException(
                        $"Method {method.Name} in {moduleType.Name} has neither 0 (no params) nor 1 (a Struct for all params) parameters.");
                case 0:
                    call = Expression.Call(Expression.Convert(moduleParam, moduleType), method);
                    schema = "{}";
                    break;
                default:
                    var paramType = parameters[0].ParameterType;
                    var fromStruct = paramType.GetMethod(nameof(IStructRepresentable<>.FromStruct),
                        BindingFlags.Public | BindingFlags.Static);
                    if (fromStruct is null)
                        throw new InvalidOperationException(
                            $"Parameter type {paramType.Name} must implement IStructRepresentable<> with static FromStruct.");

                    var paramAttr = paramType.GetCustomAttribute<ModuleCommandRequestAttribute>();
                    if (paramAttr is null)
                        throw new ArgumentException(
                            "Module command parameter must have ModuleCommandRequestAttribute which annotates its schema.");

                    schema = paramAttr.Schema;

                    var deserialized = Expression.Call(fromStruct, structParam);
                    call = Expression.Call(Expression.Convert(moduleParam, moduleType), method, deserialized);
                    break;
            }

            var lambda = Expression.Lambda<Action<IModule, Struct?>>(call, moduleParam, structParam);
            map[attr.CommandName] = new CommandCache(lambda.Compile(), schema);
        }

        return map;
    }

    public abstract string ModuleId { get; }
    public abstract BufferState GetBufferState();

    public ModuleSchema GetOperationSchema()
    {
        var schema = new ModuleSchema { ModuleId = ModuleId };
        foreach (var (cmdName, cacheEntry) in _commandMap)
        {
            schema.CommandSchemas[cmdName] = cacheEntry.Schema;
        }

        return schema;
    }

    [TraceSpan]
    public void OperateBuffer(BufferOperation op)
    {
        if (!_commandMap.TryGetValue(op.Command, out var cacheEntry))
            throw new InvalidOperationException($"Module '{ModuleId}' does not handle command '{op.Command}'.");

        var hasParam = op.Params.Fields.Count > 0;
        cacheEntry.Action(this, hasParam ? op.Params : null);
    }
}