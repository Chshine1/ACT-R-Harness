using Harness.Abstractions;
using Harness.Core.Modules;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Core.Extensions;

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.Members)]
public static class DependencyInjectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHarnessCore()
        {
            services.AddSingleton<IRewardService, HouseholdRewardService>();

            services.AddSingleton<IClock, StepClock>();

            services.AddSingleton<DeclarativeMemoryModule>();
            services.AddSingleton<IntentionModule>();
            services.AddSingleton<PerceptionMotorModule>();

            services.AddSingleton<IModuleRegistry, ModuleRegistry>(sp =>
            {
                var registry = new ModuleRegistry();

                registry.RegisterModule(sp.GetRequiredService<DeclarativeMemoryModule>());
                registry.RegisterModule(sp.GetRequiredService<IntentionModule>());

                var perceptionMotor = sp.GetRequiredService<PerceptionMotorModule>();
                registry.RegisterModule(perceptionMotor);
                registry.RegisterRewardProvider(perceptionMotor);

                return registry;
            });

            services.AddSingleton<IProceduralMemory, ProceduralMemory>();
            services.AddSingleton<INeuroCore, NeuroCore>();
            services.AddSingleton<HarnessCore>();

            return services;
        }
    }
}