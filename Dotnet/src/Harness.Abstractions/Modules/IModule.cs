using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;

namespace Harness.Abstractions.Modules;

[AttributeUsage(AttributeTargets.Method)]
public class ModuleCommandAttribute(string commandName) : Attribute
{
    public string CommandName { get; } = commandName;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ModuleCommandRequestAttribute(string schema) : Attribute
{
    public string Schema { get; } = schema;
}

public interface IStructRepresentable<out T> where T : class
{
    static abstract T FromStruct(Struct value);
}

public interface IModule
{
    string ModuleId { get; }
    BufferState GetBufferState();
    ModuleSchema GetOperationSchema();
    void OperateBuffer(BufferOperation op);
}