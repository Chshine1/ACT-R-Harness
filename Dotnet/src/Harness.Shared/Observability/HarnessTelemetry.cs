using System.Diagnostics;

namespace Harness.Shared.Observability;

public static class HarnessTelemetry
{
    public static ActivitySource ActivitySource { get; } = new("ACTR.Harness");
}
