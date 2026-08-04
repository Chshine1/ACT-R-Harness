using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Shared.Observability;
using JetBrains.Annotations;

namespace Harness.Abstractions.Modules;

[MeansImplicitUse]
[AttributeUsage(AttributeTargets.Method)]
public class ModuleCommandAttribute(string commandName) : Attribute
{
    public string CommandName { get; } = commandName;
}

[MeansImplicitUse]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ModuleCommandRequestAttribute(string schema) : Attribute
{
    public string Schema { get; } = schema;
}

public interface IStructRepresentable<out T> where T : class
{
    static abstract T FromStruct(Struct value);
    Struct ToStruct();
}

public interface IModule
{
    string ModuleId { get; }

    [TraceSpan]
    BufferState GetBufferState();

    [TraceSpan]
    ModuleSchema GetOperationSchema();

    [TraceSpan]
    void OperateBuffer(BufferOperation op);
}