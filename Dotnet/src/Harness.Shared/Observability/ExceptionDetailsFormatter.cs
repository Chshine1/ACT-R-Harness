using System.Text;

namespace Harness.Shared.Observability;

public static class ExceptionDetailsFormatter
{
    public static IReadOnlyDictionary<string, object?> ToDictionary(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var details = new Dictionary<string, object?>
        {
            ["errorType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["errorMessage"] = exception.Message,
            ["errorStackTrace"] = exception.StackTrace,
            ["errorDetails"] = exception.ToString(),
            ["errorSummary"] = BuildSummary(exception)
        };

        var innerExceptions = FlattenInnerExceptions(exception).ToList();
        if (innerExceptions.Count > 0)
        {
            details["innerExceptions"] = innerExceptions;
        }

        return details;
    }

    public static string BuildSummary(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        var chain = FlattenExceptionChain(exception).ToList();
        for (var index = 0; index < chain.Count; index++)
        {
            var current = chain[index];
            if (index > 0)
            {
                builder.Append(" --> ");
            }

            builder.Append(current.GetType().FullName ?? current.GetType().Name);
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                builder.Append(": ");
                builder.Append(current.Message);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<Dictionary<string, object?>> FlattenInnerExceptions(Exception exception)
    {
        foreach (var inner in FlattenExceptionChain(exception).Skip(1))
        {
            yield return new Dictionary<string, object?>
            {
                ["errorType"] = inner.GetType().FullName ?? inner.GetType().Name,
                ["errorMessage"] = inner.Message,
                ["errorStackTrace"] = inner.StackTrace,
                ["errorDetails"] = inner.ToString()
            };
        }
    }

    private static IEnumerable<Exception> FlattenExceptionChain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
