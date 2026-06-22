namespace Harness.Abstractions;

public interface IModuleRegistry
{
    void RegisterModule(IModule module);
    IReadOnlyList<IModule> GetModules();
}