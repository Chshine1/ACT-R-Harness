using Harness.Abstractions;
using Harness.Abstractions.Modules;
using Harness.Abstractions.Reward;

namespace Harness.Core;

public class ModuleRegistry : IModuleRegistry
{
    private readonly List<IModule> _modules = [];
    private readonly List<IRewardStateProvider> _rewardProviders = [];

    public void RegisterModule(IModule module)
    {
        _modules.Add(module);
    }

    public void RegisterRewardProvider(IRewardStateProvider rewardProvider)
    {
        _rewardProviders.Add(rewardProvider);
    }

    public IReadOnlyCollection<IModule> GetModules()
    {
        return _modules;
    }

    public IReadOnlyCollection<IRewardStateProvider> GetRewardProviders()
    {
        return _rewardProviders;
    }
}