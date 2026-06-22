using Actr.Services;
using Grpc.Net.Client;
using Harness.Core.Extensions;
using Harness.Host.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Harness.Host;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature, ImplicitUseTargetFlags.Itself)]
public class Program
{
    public static void Main(string[] args)
    {
        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<GrpcClientsOptions>(context.Configuration.GetSection(GrpcClientsOptions.Section));

                services.AddSingleton(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<GrpcClientsOptions>>().Value;
                    var channel = GrpcChannel.ForAddress(opts.DeclarativeMemoryAddress);
                    return new DeclarativeMemory.DeclarativeMemoryClient(channel);
                });
                services.AddSingleton(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<GrpcClientsOptions>>().Value;
                    var channel = GrpcChannel.ForAddress(opts.FrostpunkWorldAddress);
                    return new FrostpunkWorld.FrostpunkWorldClient(channel);
                });
                services.AddSingleton(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<GrpcClientsOptions>>().Value;
                    var channel = GrpcChannel.ForAddress(opts.ProceduralMemoryAddress);
                    return new ProceduralMemory.ProceduralMemoryClient(channel);
                });
                services.AddSingleton(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<GrpcClientsOptions>>().Value;
                    var channel = GrpcChannel.ForAddress(opts.NeuroCoreAddress);
                    return new NeuroCore.NeuroCoreClient(channel);
                });

                services.AddHarnessCore();
                
                services.AddHostedService<HarnessRunner>();
            })
            .Build();

        host.Run();
    }
}