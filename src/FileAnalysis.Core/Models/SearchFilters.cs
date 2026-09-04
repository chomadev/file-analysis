namespace FileAnalysis.Core.Models;

/// <summary>Optional filters applied on top of a search or similarity query.</summary>
public class SearchFilters
{
    public string? Extension { get; set; }
    public string? Tag { get; set; }
    public string? Category { get; set; }
    public DateTime? Since { get; set; }

    public static readonly SearchFilters None = new();
}
