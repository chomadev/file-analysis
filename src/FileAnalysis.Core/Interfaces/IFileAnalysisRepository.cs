using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence for AI-generated file analyses.</summary>
public interface IFileAnalysisRepository
{
    Task<FileAnalysisResult?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default);
    Task UpsertAsync(FileAnalysisResult analysis, CancellationToken ct = default);
}
