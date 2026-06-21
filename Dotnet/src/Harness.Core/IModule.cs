using Actr;

namespace Harness.Core;

public interface IModule
{
    string ModuleId { get; }
    
    BufferState GetBufferState();
    
    ModuleSchema GetOperationSchema();
    void OperateBuffer(BufferOperation op);
}