using System.Diagnostics;

namespace Harness.Abstractions.Observability;

public static class HarnessTelemetry
{
    public static ActivitySource ActivitySource { get; } = new("ACTR.Harness");
}
