using Harness.Core;
using Microsoft.Extensions.Hosting;

namespace Harness.Host;

public class HarnessRunner(HarnessCore core) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await core.StepAsync(null);
            await Task.Delay(100, stoppingToken);
        }
    }
}