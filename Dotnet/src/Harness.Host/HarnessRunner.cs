using Harness.Host.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class HarnessRunner(HarnessEpochExecutor epochExecutor, IOptions<HarnessOptions> options)
    : BackgroundService
{
    private readonly HarnessOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        for (var epoch = 0; epoch < _options.MaxEpochs && !ct.IsCancellationRequested; epoch++)
        {
            await epochExecutor.RunAsync(ct);
        }
    }
}
