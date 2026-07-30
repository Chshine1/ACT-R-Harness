using Grpc.Core;
using Harness.Abstractions.Observability;

namespace Harness.Core.Observability;

public sealed class GrpcObservabilityCall : IDisposable
{
    private const string RunIdHeader = "x-harness-run-id";
    private const string EpochHeader = "x-harness-epoch";
    private const string StepHeader = "x-harness-step";
    private const string CorrelationIdHeader = "x-harness-correlation-id";
    private const string OperationHeader = "x-harness-operation";

    private readonly IDisposable _scope;

    private GrpcObservabilityCall(Metadata headers, IDisposable scope)
    {
        Headers = headers;
        _scope = scope;
    }

    public Metadata Headers { get; }

    public static GrpcObservabilityCall Begin(string operation)
    {
        var correlationId = $"{operation}:{Guid.NewGuid():N}";
        var scope = HarnessExecutionContext.Push(correlationId: correlationId, operation: operation);
        var context = HarnessExecutionContext.Current;

        var headers = new Metadata();
        AddHeader(headers, RunIdHeader, context.RunId);
        AddHeader(headers, EpochHeader, context.Epoch?.ToString());
        AddHeader(headers, StepHeader, context.Step?.ToString());
        AddHeader(headers, CorrelationIdHeader, context.CorrelationId);
        AddHeader(headers, OperationHeader, context.Operation);

        return new GrpcObservabilityCall(headers, scope);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    private static void AddHeader(Metadata headers, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers.Add(name, value);
        }
    }
}
