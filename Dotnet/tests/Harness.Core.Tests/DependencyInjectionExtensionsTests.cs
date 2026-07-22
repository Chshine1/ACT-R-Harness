using Grpc.Net.Client;
using Harness.Abstractions;
using Harness.Abstractions.Actr.Services;
using Harness.Abstractions.Modules;
using Harness.Codebase.Modules;
using Harness.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Core.Tests;

public class DependencyInjectionExtensionsTests
{
    [Fact]
    public void AddHarnessCore_registers_modules_required_by_active_rules()
    {
        using var provider = CreateProvider();
        var registry = provider.GetRequiredService<IModuleRegistry>();
        var modules = registry.GetModules().ToDictionary(module => module.ModuleId);

        Assert.Contains("declarative_memory", modules.Keys);
        Assert.Contains("intention", modules.Keys);
        Assert.Contains("file_explorer", modules.Keys);
        Assert.Contains("code_viewport", modules.Keys);

        Assert.IsType<FileExplorerModule>(provider.GetRequiredService<FileExplorerModule>());
        Assert.IsType<CodeViewportModule>(provider.GetRequiredService<CodeViewportModule>());
    }

    [Fact]
    public async Task AddHarnessCore_registers_a_minimal_embedding_service()
    {
        using var provider = CreateProvider();
        var embeddings = await provider
            .GetRequiredService<IEmbeddingService>()
            .GetEmbeddingsAsync(["alpha", "alpha", "beta"]);

        Assert.Equal(3, embeddings.Length);
        Assert.All(embeddings, vector => Assert.Equal(16, vector.Length));
        Assert.Equal(embeddings[0], embeddings[1]);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DeclarativeMemory.DeclarativeMemoryClient(GrpcChannel.ForAddress("http://localhost")));
        services.AddHarnessCore();

        return services.BuildServiceProvider();
    }
}
