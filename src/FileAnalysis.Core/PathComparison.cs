namespace FileAnalysis.Core;

/// <summary>OS-appropriate comparison for filesystem paths. Windows and (by default) macOS filesystems are
/// case-insensitive but case-preserving — the same physical file can be referenced with different casing
/// across scans (e.g. a differently-cased drive letter). Linux filesystems are case-sensitive, where two
/// differently-cased paths are genuinely different files. This is a heuristic based on the OS the app is
/// running on, not a guarantee about any specific mounted volume's actual case sensitivity — the same
/// tradeoff tools like MSBuild and NuGet accept, since there's no cheap, portable way to ask "is this exact
/// path case-sensitive" per volume.</summary>
public static class PathComparison
{
    public static readonly bool IsCaseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    public static readonly StringComparer Comparer = IsCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static readonly StringComparison Comparison = IsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
