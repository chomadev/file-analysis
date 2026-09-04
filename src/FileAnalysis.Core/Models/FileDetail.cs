namespace FileAnalysis.Core.Models;

/// <summary>Full detail for a single indexed file: metadata, AI analysis, and extracted content.</summary>
public record FileDetail(IndexedFile File, FileAnalysisResult? Analysis, IReadOnlyList<FileContent> Contents);
