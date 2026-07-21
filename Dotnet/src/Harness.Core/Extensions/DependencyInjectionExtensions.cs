using Harness.Abstractions;
using Harness.Abstractions.Modules;
using Harness.Abstractions.Reward;
using Harness.Core.Modules;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Core.Extensions;

public class MockRewardService : IRewardService
{
    public Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0f);
    }
}

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.Members)]
public static class DependencyInjectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHarnessCore()
        {
            services.AddSingleton<IRewardService, MockRewardService>();

            services.AddSingleton<IClock, StepClock>();

            services.AddSingleton<DeclarativeMemoryModule>();
            services.AddSingleton<IntentionModule>();

            services.AddSingleton<IModuleRegistry, ModuleRegistry>(sp =>
            {
                var registry = new ModuleRegistry();

                registry.RegisterModule(sp.GetRequiredService<DeclarativeMemoryModule>());
                registry.RegisterModule(sp.GetRequiredService<IntentionModule>());

                return registry;
            });

            services.AddSingleton<IProceduralMemory, ProceduralMemory>();
            services.AddSingleton<INeuroCore, NeuroCore>();
            services.AddSingleton<HarnessCore>();

            return services;
        }
    }
}