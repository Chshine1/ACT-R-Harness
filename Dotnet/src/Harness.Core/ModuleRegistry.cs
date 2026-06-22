using Harness.Abstractions;

namespace Harness.Core;

public class ModuleRegistry : IModuleRegistry
{
    private readonly List<IModule> _modules = [];

    public void RegisterModule(IModule module)
    {
        _modules.Add(module);
    }

    public IReadOnlyList<IModule> GetModules()
    {
        return _modules;
    }
}