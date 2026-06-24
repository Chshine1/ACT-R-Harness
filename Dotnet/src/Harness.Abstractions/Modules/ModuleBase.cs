using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Type = System.Type;

namespace Harness.Abstractions.Modules;

public abstract class ModuleBase : IModule
{
    private record struct CommandCache(Action<IModule, Struct?> Action, string Schema);
    
    private static readonly ConcurrentDictionary<Type, Dictionary<string, CommandCache>> cache = new();
    private readonly Dictionary<string, CommandCache> _commandMap;

    protected ModuleBase()
    {
        var type = GetType();
        _commandMap = cache.GetOrAdd(type, static t => BuildCommandMap(t));
    }

    private static Dictionary<string, CommandCache> BuildCommandMap(Type moduleType)
    {
        var map = new Dictionary<string, CommandCache>();
        var methods = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<ModuleCommandAttribute>();
            if (attr is null) continue;

            var parameters = method.GetParameters();
            if (parameters.Length > 1)
                throw new InvalidOperationException(
                    $"Method {method.Name} in {moduleType.Name} has more than one parameter.");

            var moduleParam = Expression.Parameter(typeof(IModule));
            var structParam = Expression.Parameter(typeof(Struct));

            Expression call;
            string schema;
            if (parameters.Length == 0)
            {
                call = Expression.Call(Expression.Convert(moduleParam, moduleType), method);
                schema = "{}";
            }
            else
            {
                var paramType = parameters[0].ParameterType;
                var fromStruct = paramType.GetMethod("FromStruct", BindingFlags.Public | BindingFlags.Static);
                if (fromStruct is null)
                    throw new InvalidOperationException(
                        $"Parameter type {paramType.Name} must implement IStructRepresentable<> with static FromStruct.");

                var paramAttr = paramType.GetCustomAttribute<ModuleCommandRequestAttribute>();
                if (paramAttr is null)
                    throw new ArgumentException("Module command parameter must have ModuleCommandRequestAttribute which annotates its schema.");
                
                schema = paramAttr.Schema;

                var deserialized = Expression.Call(fromStruct, structParam);
                call = Expression.Call(Expression.Convert(moduleParam, moduleType), method, deserialized);
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
        foreach (var (cmdName, c) in _commandMap)
        {
            schema.CommandSchemas[cmdName] = c.Schema;
        }
        return schema;
    }

    public void OperateBuffer(BufferOperation op)
    {
        if (!_commandMap.TryGetValue(op.Command, out var c))
            throw new InvalidOperationException(
                $"Module '{ModuleId}' does not handle command '{op.Command}'.");
        var hasParam = op.Params.Fields.Count > 0;
        c.Action(this, hasParam ? op.Params : null);
    }
}