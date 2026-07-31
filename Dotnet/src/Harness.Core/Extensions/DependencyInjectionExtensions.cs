using Harness.Abstractions;
using Harness.Abstractions.Modules;
using Harness.Abstractions.Reward;
using Harness.Codebase.Modules;
using Harness.Core.Rules;
using Harness.Core.Modules;
using Harness.Core.NeuroCore;
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
            services.AddSingleton<IEmbeddingService, DeterministicEmbeddingService>();
            services.AddSingleton<RulesLoader>();
            services.AddSingleton<DeclarativeMemoryService>();
            services.AddSingleton<LlmClient>();
            services.AddSingleton<SymbolicMatcher>();
            services.AddSingleton<FuzzyConditionEvaluator>();
            services.AddSingleton<ConditionEvaluator>();
            services.AddSingleton<ActionResolver>();
            services.AddSingleton<Func<IReadOnlyCollection<IModule>>>(sp =>
                () => sp.GetRequiredService<IModuleRegistry>().GetModules());

            services.AddSingleton<DeclarativeMemoryModule>();
            services.AddSingleton<IntentionModule>();
            services.AddSingleton<FileExplorerModule>();
            services.AddSingleton<CodeViewportModule>();

            services.AddSingleton<IModuleRegistry, ModuleRegistry>(sp =>
            {
                var registry = new ModuleRegistry();

                registry.RegisterModule(sp.GetRequiredService<DeclarativeMemoryModule>());
                registry.RegisterModule(sp.GetRequiredService<IntentionModule>());
                registry.RegisterModule(sp.GetRequiredService<FileExplorerModule>());
                registry.RegisterModule(sp.GetRequiredService<CodeViewportModule>());

                return registry;
            });

            services.AddSingleton<IProceduralMemory, ProceduralMemory>();
            services.AddSingleton<INeuroCore, NeuroCore.NeuroCore>();
            services.AddSingleton<HarnessCore>();

            return services;
        }
    }
}
