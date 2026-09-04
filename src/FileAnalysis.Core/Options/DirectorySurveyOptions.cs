namespace FileAnalysis.Core.Options;

/// <summary>Configuration for the pre-indexing directory structure survey.</summary>
public class DirectorySurveyOptions
{
    public const string SectionName = "DirectorySurvey";

    public bool Enabled { get; set; } = true;

    /// <summary>How deep to walk (relative to the scan root) before just rolling counts up into the deepest surveyed ancestor.</summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>Directories at or above this file count that aren't heuristic-excluded get an AI classification call.</summary>
    public int LargeFileCountThreshold { get; set; } = 200;

    /// <summary>Directories at or above this size that aren't heuristic-excluded get an AI classification call.</summary>
    public long LargeSizeBytesThreshold { get; set; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>When true, accept the AI's suggested classification for undecided directories instead of prompting interactively.</summary>
    public bool AutoConfirm { get; set; } = false;
}
