using Harness.Codebase.Configuration;
using Harness.Core.Configuration;
using Harness.Core.Extensions;
using Harness.Host.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Harness.Host;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature, ImplicitUseTargetFlags.Itself)]
public class Program
{
    public static void Main(string[] args)
    {
        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddJsonConsole();
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<HarnessOptions>(context.Configuration.GetSection(HarnessOptions.Section));
                services.Configure<FileExplorerOptions>(context.Configuration.GetSection(FileExplorerOptions.Section));
                services.Configure<ProceduralMemoryOptions>(
                    context.Configuration.GetSection(ProceduralMemoryOptions.Section));
                services.Configure<DeclarativeMemoryOptions>(
                    context.Configuration.GetSection(DeclarativeMemoryOptions.Section));
                services.Configure<IntentionOptions>(context.Configuration.GetSection(IntentionOptions.Section));
                services.Configure<LlmClientOptions>(context.Configuration.GetSection(LlmClientOptions.Section));

                services.AddHarnessCore();
                services.AddHostedService<HarnessRunner>();
            })
            .Build();

        host.Run();
    }
}