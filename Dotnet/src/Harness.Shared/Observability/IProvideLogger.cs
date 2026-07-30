using Microsoft.Extensions.Logging;

namespace Harness.Shared.Observability;

public interface IProvideLogger
{
    ILogger Logger { get; }
}
