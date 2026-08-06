using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Harness.Shared.Observability;

public sealed class RunReportWriter(ILogger<RunReportWriter> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string? Write(RunReport report, string artifactRoot)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);

        var runDirectory = Path.Combine(
            Path.GetFullPath(artifactRoot),
            "runs",
            report.RunId);
        var reportPath = Path.Combine(runDirectory, "summary.json");
        var temporaryPath = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(runDirectory);
            var json = JsonSerializer.Serialize(report, SerializerOptions);
            File.WriteAllText(temporaryPath, json, Encoding.UTF8);
            File.Move(temporaryPath, reportPath, overwrite: true);

            LoggingModel.Log(
                logger,
                GetLogLevel(report.Status),
                LoggingModel.Events.RunReportWritten,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.RunId,
                        report.RunId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ReportStatus,
                        report.Status),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.StopReason,
                        report.StopReason),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ArtifactPath,
                        reportPath)
                });

            return reportPath;
        }
        catch (Exception exception)
        {
            LoggingModel.LogException(
                logger,
                nameof(RunReportWriter.Write),
                exception,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.RunId,
                        report.RunId),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ArtifactPath,
                        reportPath),
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ReportStatus,
                        report.Status)
                });

            TryDelete(temporaryPath);
            return null;
        }
    }

    private static LogLevel GetLogLevel(string status) =>
        status switch
        {
            "failed" => LogLevel.Error,
            "canceled" => LogLevel.Warning,
            _ => LogLevel.Information
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original report write failure.
        }
    }
}
