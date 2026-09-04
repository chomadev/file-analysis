using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.CLI;

/// <summary>Reports scan progress to the console.</summary>
public class ConsoleScanProgressReporter(bool verbose) : IScanProgressReporter
{
    public void ReportProgress(string filePath, ScanStatus status, string? message = null)
    {
        if (!verbose) return;
        var label = status switch
        {
            ScanStatus.New       => "[NEW]      ",
            ScanStatus.Updated   => "[UPDATED]  ",
            ScanStatus.Unchanged => "[UNCHANGED]",
            ScanStatus.Skipped   => "[SKIPPED]  ",
            ScanStatus.Error     => "[ERROR]    ",
            _                    => "[?]        "
        };
        var detail = message is not null ? $" — {message}" : string.Empty;
        Console.WriteLine($"  {label} {filePath}{detail}");
    }

    public void ReportSummary(ScanSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine($"  {summary.New} new  |  {summary.Updated} updated  |  {summary.Unchanged} unchanged  |  {summary.Skipped} skipped  |  {summary.Errors} errors");
        Console.WriteLine($"  Done in {summary.Elapsed.TotalSeconds:F1}s");
    }
}
