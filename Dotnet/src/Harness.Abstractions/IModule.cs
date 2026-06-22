using Harness.Abstractions.Actr;

namespace Harness.Abstractions;

public interface IModule
{
    string ModuleId { get; }
    
    BufferState GetBufferState();
    
    ModuleSchema GetOperationSchema();
    void OperateBuffer(BufferOperation op);
}