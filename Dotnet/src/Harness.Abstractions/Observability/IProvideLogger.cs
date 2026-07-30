using Microsoft.Extensions.Logging;

namespace Harness.Abstractions.Observability;

public interface IProvideLogger
{
    ILogger Logger { get; }
}
