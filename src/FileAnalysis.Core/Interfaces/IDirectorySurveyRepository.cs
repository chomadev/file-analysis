using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence for directory survey results.</summary>
public interface IDirectorySurveyRepository
{
    Task UpsertAsync(DirectorySurveyEntry entry, CancellationToken ct = default);
    Task<DirectorySurveyEntry?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<DirectorySurveyEntry>> ListByRootAsync(string scanRootPath, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetExcludedPathsAsync(string scanRootPath, CancellationToken ct = default);
}
