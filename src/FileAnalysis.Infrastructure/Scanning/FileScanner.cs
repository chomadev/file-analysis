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

    /// <summary>Scans <paramref name="rootPath"/> and upserts file records.</summary>
    public async Task<ScanSummary> ScanAsync(string rootPath, bool dryRun = false, bool skipAi = false, bool reanalyze = false,
        IReadOnlySet<string>? excludedPaths = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        int newCount = 0, updated = 0, unchanged = 0, skipped = 0, errors = 0;

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

                var hash = ComputeMd5(filePath);
                var existing = dryRun ? null : await repository.GetByPathAsync(filePath, ct);

                var mimeType = MimeTypeDetector.Detect(filePath);
                var isUnchanged = existing is not null
                    && existing.Md5Hash == hash
                    && existing.SizeBytes == info.Length;

                if (isUnchanged)
                {
                    // Re-extract content if it was never stored (self-healing across phase upgrades).
                    // Files with no extractable content (e.g. .3mf) always report zero chunks, so we also
                    // gate on "already analyzed at least once" — otherwise a content-less file would
                    // re-extract and re-enqueue for AI analysis on every single scan forever.
                    var hasContent = !dryRun
                        && await contentRepo.GetChunksAsync(existing!.Id, ct) is { Count: > 0 };
                    var alreadyAnalyzed = existing!.Analysis is not null;

                    if (hasContent || alreadyAnalyzed)
                    {
                        if (!dryRun && !skipAi && (reanalyze || !alreadyAnalyzed))
                            EnqueueAnalysis(existing!.Id);

                        reporter.ReportProgress(filePath, ScanStatus.Unchanged);
                        unchanged++;
                        continue;
                    }

                    if (!dryRun)
                    {
                        await ExtractAndStoreAsync(filePath, mimeType, existing!.Id, ct);
                        if (!skipAi)
                            EnqueueAnalysis(existing!.Id);
                    }

                    reporter.ReportProgress(filePath, ScanStatus.Unchanged);
                    unchanged++;
                    continue;
                }

                var isNew = existing is null;
                var record = existing ?? new IndexedFile
                {
                    Path = filePath,
                    Name = info.Name,
                };

                record.Name = info.Name;
                record.Extension = info.Extension.ToLowerInvariant();
                record.MimeType = mimeType;
                record.SizeBytes = info.Length;
                record.Md5Hash = hash;
                record.FileCreatedAt = info.CreationTimeUtc;
                record.FileModifiedAt = info.LastWriteTimeUtc;
                record.LastScannedAt = DateTime.UtcNow;
                if (isNew) record.IndexedAt = DateTime.UtcNow;

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
