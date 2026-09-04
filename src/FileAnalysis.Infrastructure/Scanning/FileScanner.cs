using System.Security.Cryptography;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using FileAnalysis.Infrastructure.Analysis;
using FileAnalysis.Infrastructure.Extraction;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Scanning;

/// <summary>Recursively scans a directory and indexes file metadata.</summary>
public class FileScanner(
    IFileRepository repository,
    IScanProgressReporter reporter,
    IOptions<ScannerOptions> options,
    ContentExtractorDispatcher dispatcher,
    IFileContentRepository contentRepo,
    ContentChunker chunker,
    AnalysisQueue analysisQueue)
{
    private readonly ScannerOptions _options = options.Value;

    /// <summary>Scans <paramref name="rootPath"/> and upserts file records. By default, a file whose size
    /// and mtime both match its last-indexed values is treated as unchanged without re-hashing it — pass
    /// <paramref name="rehash"/> to force the old always-verify-by-MD5 behavior (needed to catch a content
    /// edit that preserves mtime).</summary>
    public async Task<ScanSummary> ScanAsync(string rootPath, bool dryRun = false, bool skipAi = false, bool reanalyze = false,
        IReadOnlySet<string>? excludedPaths = null, bool rehash = false, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        int newCount = 0, updated = 0, unchanged = 0, skipped = 0, errors = 0;

        // Bulk-preload existing file metadata for everything under this root in one round trip instead of
        // a per-file GetByPathAsync query. AsNoTracking projection only — see GetScanMetadataAsync's doc
        // comment for why tracked entities would make later SaveChanges calls go quadratic.
        var existingByPath = dryRun
            ? new Dictionary<string, FileScanMetadata>(FileAnalysis.Core.PathComparison.Comparer)
            : await repository.GetScanMetadataAsync(rootPath, ct);

        foreach (var filePath in EnumerateFiles(rootPath, excludedPaths))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                if (info.Length > _options.MaxFileSizeBytes)
                {
                    reporter.ReportProgress(filePath, ScanStatus.Skipped, "exceeds size limit");
                    skipped++;
                    continue;
                }

                existingByPath.TryGetValue(filePath, out var meta);

                // Fast path: size and mtime unchanged since last index means we skip reading (and hashing)
                // up to MaxFileSizeBytes off disk for a file we already know is unchanged. This changes
                // default change-detection semantics — a content edit that preserves mtime would be missed —
                // so --rehash opts back into always computing and comparing MD5.
                // Tolerance (not exact equality) on the mtime comparison: Postgres "timestamp" has
                // microsecond resolution while NTFS mtimes carry 100ns ticks, so a value round-tripped
                // through the DB can differ from the freshly-read FileInfo value by a sub-microsecond
                // rounding error even when the file itself hasn't changed.
                var mtimeMatch = !rehash && meta?.FileModifiedAt is { } storedMtime
                    && meta.SizeBytes == info.Length
                    && (info.LastWriteTimeUtc - storedMtime).Duration() < TimeSpan.FromMilliseconds(1);

                string hash;
                bool isUnchanged;
                if (mtimeMatch)
                {
                    hash = meta!.Md5Hash ?? "";
                    isUnchanged = true;
                }
                else
                {
                    hash = ComputeMd5(filePath);
                    isUnchanged = meta is not null && meta.Md5Hash == hash && meta.SizeBytes == info.Length;
                }

                var mimeType = MimeTypeDetector.Detect(filePath);

                if (isUnchanged)
                {
                    // Re-extract content if it was never stored (self-healing across phase upgrades).
                    // Files with no extractable content (e.g. .3mf) always report zero chunks, so we also
                    // gate on "already analyzed at least once" — otherwise a content-less file would
                    // re-extract and re-enqueue for AI analysis on every single scan forever.
                    var hasContent = meta!.HasChunks;
                    var alreadyAnalyzed = meta.HasAnalysis;

                    if (hasContent || alreadyAnalyzed)
                    {
                        if (!dryRun && !skipAi && (reanalyze || !alreadyAnalyzed))
                            EnqueueAnalysis(meta.Id);

                        reporter.ReportProgress(filePath, ScanStatus.Unchanged);
                        unchanged++;
                        continue;
                    }

                    if (!dryRun)
                    {
                        await ExtractAndStoreAsync(filePath, mimeType, meta.Id, ct);
                        if (!skipAi)
                            EnqueueAnalysis(meta.Id);
                    }

                    reporter.ReportProgress(filePath, ScanStatus.Unchanged);
                    unchanged++;
                    continue;
                }

                var isNew = meta is null;
                var record = new IndexedFile
                {
                    Path = filePath,
                    Name = info.Name,
                    Extension = info.Extension.ToLowerInvariant(),
                    MimeType = mimeType,
                    SizeBytes = info.Length,
                    Md5Hash = hash,
                    FileCreatedAt = info.CreationTimeUtc,
                    FileModifiedAt = info.LastWriteTimeUtc,
                    LastScannedAt = DateTime.UtcNow,
                };
                if (isNew) record.IndexedAt = DateTime.UtcNow;

                // Written per-file (not batched) so a failure processing file N never affects files 1..N-1.
                IndexedFile persisted = record;
                if (!dryRun)
                {
                    persisted = await repository.UpsertAsync(record, ct);
                    await ExtractAndStoreAsync(filePath, mimeType, persisted.Id, ct);
                    if (!skipAi)
                        EnqueueAnalysis(persisted.Id);
                }

                var status = isNew ? ScanStatus.New : ScanStatus.Updated;
                reporter.ReportProgress(filePath, status);
                if (isNew) newCount++; else updated++;
            }
            catch (Exception ex)
            {
                reporter.ReportProgress(filePath, ScanStatus.Error, ex.Message);
                errors++;
            }
        }

        var summary = new ScanSummary(newCount, updated, unchanged, skipped, errors,
            DateTime.UtcNow - started);
        reporter.ReportSummary(summary);
        return summary;
    }

    private void EnqueueAnalysis(Guid fileId) => analysisQueue.Writer.TryWrite(fileId);

    private async Task ExtractAndStoreAsync(string filePath, string mimeType, Guid fileId, CancellationToken ct)
    {
        var rawText = await dispatcher.ExtractAsync(filePath, mimeType, ct);
        if (rawText is not null)
        {
            var chunks = chunker.Chunk(rawText);
            await contentRepo.SaveChunksAsync(fileId, chunks, ct);
        }
    }

    private IEnumerable<string> EnumerateFiles(string root, IReadOnlySet<string>? excludedPaths)
    {
        if (!Directory.Exists(root))
            yield break;

        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch { continue; }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    var dirName = Path.GetFileName(entry);
                    if (_options.ExcludePatterns.Any(p => GlobMatcher.Matches(p, dirName)))
                        continue;
                    if (excludedPaths?.Contains(entry) == true)
                        continue;
                    queue.Enqueue(entry);
                }
                else
                {
                    var fileName = Path.GetFileName(entry);
                    if (_options.ExcludePatterns.Any(p => GlobMatcher.Matches(p, fileName)))
                        continue;
                    yield return entry;
                }
            }
        }
    }

    private static string ComputeMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = MD5.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
