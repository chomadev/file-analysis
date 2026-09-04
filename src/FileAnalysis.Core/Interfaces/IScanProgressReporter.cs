namespace FileAnalysis.Core.Interfaces;

/// <summary>Reports scan progress to the caller.</summary>
public interface IScanProgressReporter
{
    void ReportProgress(string filePath, ScanStatus status, string? message = null);
    void ReportSummary(ScanSummary summary);
}

public enum ScanStatus { New, Updated, Unchanged, Skipped, Error }

public record ScanSummary(int New, int Updated, int Unchanged, int Skipped, int Errors, TimeSpan Elapsed);
