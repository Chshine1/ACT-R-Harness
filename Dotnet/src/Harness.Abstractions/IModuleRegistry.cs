namespace Harness.Abstractions;

public interface IModuleRegistry
{
    void RegisterModule(IModule module);
    IReadOnlyCollection<IModule> GetModules();
}