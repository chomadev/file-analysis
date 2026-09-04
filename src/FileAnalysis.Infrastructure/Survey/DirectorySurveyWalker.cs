using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure.Scanning;

namespace FileAnalysis.Infrastructure.Survey;

/// <summary>
/// Pure, I/O-only directory structure walker — no AI, no DB. Computes per-directory file/size stats
/// up to a max depth and applies heuristic (name-based) classification, never recursing into a
/// heuristic-excluded subtree directory-by-directory (its totals are still counted for the report).
/// </summary>
public static class DirectorySurveyWalker
{
    public static List<DirectorySurveyEntry> Walk(string rootPath, string[] excludePatterns, int maxDepth)
    {
        var entries = new List<DirectorySurveyEntry>();
        if (Directory.Exists(rootPath))
            WalkDirectory(rootPath, rootPath, 0, excludePatterns, maxDepth, entries);
        return entries;
    }

    private static (int Files, long Size, Dictionary<string, int> ExtCounts) WalkDirectory(
        string dirPath, string scanRoot, int depth, string[] excludePatterns, int maxDepth, List<DirectorySurveyEntry> entries)
    {
        int directFileCount = 0;
        long directSizeBytes = 0;
        var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var subdirs = new List<string>();

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dirPath))
            {
                if (Directory.Exists(entry))
                {
                    subdirs.Add(entry);
                    continue;
                }

                long size;
                try { size = new FileInfo(entry).Length; }
                catch { continue; }

                directFileCount++;
                directSizeBytes += size;
                var ext = Path.GetExtension(entry);
                if (!string.IsNullOrEmpty(ext))
                    extCounts[ext] = extCounts.GetValueOrDefault(ext) + 1;
            }
        }
        catch
        {
            return (0, 0, extCounts);
        }

        var totalFileCount = directFileCount;
        var totalSizeBytes = directSizeBytes;

        foreach (var subdir in subdirs)
        {
            var subdirName = Path.GetFileName(subdir);
            var isJunkByName = excludePatterns.Any(p => GlobMatcher.Matches(p, subdirName));

            if (isJunkByName || depth >= maxDepth)
            {
                var (subFiles, subSize) = CountRecursive(subdir);
                totalFileCount += subFiles;
                totalSizeBytes += subSize;

                if (isJunkByName)
                {
                    entries.Add(new DirectorySurveyEntry
                    {
                        Path = subdir,
                        ScanRootPath = scanRoot,
                        Depth = depth + 1,
                        DirectFileCount = 0,
                        TotalFileCount = subFiles,
                        TotalSizeBytes = subSize,
                        Classification = DirectoryClassification.LikelyJunk,
                        ClassificationSource = ClassificationSource.Heuristic,
                        ClassificationReason = "Directory name matches a known exclude pattern",
                        ExcludeDecision = true,
                    });
                }
                continue;
            }

            var (childFiles, childSize, childExts) = WalkDirectory(subdir, scanRoot, depth + 1, excludePatterns, maxDepth, entries);
            totalFileCount += childFiles;
            totalSizeBytes += childSize;
            foreach (var kv in childExts)
                extCounts[kv.Key] = extCounts.GetValueOrDefault(kv.Key) + kv.Value;
        }

        var topExtensions = extCounts.OrderByDescending(kv => kv.Value).Take(5).Select(kv => kv.Key).ToArray();

        entries.Add(new DirectorySurveyEntry
        {
            Path = dirPath,
            ScanRootPath = scanRoot,
            Depth = depth,
            DirectFileCount = directFileCount,
            DirectSizeBytes = directSizeBytes,
            TotalFileCount = totalFileCount,
            TotalSizeBytes = totalSizeBytes,
            TopExtensions = topExtensions,
        });

        return (totalFileCount, totalSizeBytes, extCounts);
    }

    private static (int Files, long Size) CountRecursive(string dir)
    {
        int files = 0;
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(f).Length;
                    files++;
                }
                catch { /* unreadable file — skip */ }
            }
        }
        catch { /* unreadable subtree — skip */ }
        return (files, size);
    }
}
