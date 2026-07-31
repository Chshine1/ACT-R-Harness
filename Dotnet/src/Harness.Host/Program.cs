using Harness.Core.Configuration;
using Harness.Core.Observability;
using Harness.Core.Extensions;
using Harness.Host.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Harness.Host;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature, ImplicitUseTargetFlags.Itself)]
public class Program
{
    public static void Main(string[] args)
    {
        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<HarnessOptions>(context.Configuration.GetSection(HarnessOptions.Section));
                services.Configure<ProceduralMemoryOptions>(context.Configuration.GetSection(ProceduralMemoryOptions.Section));
                services.Configure<DeclarativeMemoryOptions>(context.Configuration.GetSection(DeclarativeMemoryOptions.Section));
                services.Configure<LlmClientOptions>(context.Configuration.GetSection(LlmClientOptions.Section));

                services.AddHarnessCore();
                services.AddSingleton<IObservabilityEventSink, StructuredObservabilitySink>();
                services.AddSingleton<DemoScenarioSeeder>();
                services.AddSingleton<RunArtifactsWriter>();
                services.AddHostedService<HarnessRunner>();
            })
            .Build();

        host.Run();
    }
}
