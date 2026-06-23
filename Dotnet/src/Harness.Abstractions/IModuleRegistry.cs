namespace Harness.Abstractions;

public interface IModuleRegistry
{
    void RegisterModule(IModule module);
    void RegisterRewardProvider(IRewardStateProvider rewardProvider);
    IReadOnlyCollection<IModule> GetModules();
    IReadOnlyCollection<IRewardStateProvider> GetRewardProviders();
}