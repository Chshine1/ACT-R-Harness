using Harness.Abstractions;
using Harness.Core;
using Microsoft.Extensions.Hosting;

namespace Harness.Host;

public class HarnessRunner(HarnessCore core, IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await core.StepAsync();
            await clock.TickAsync(0f, cancellationToken);
            await Task.Delay(10, cancellationToken);
        }
    }
}