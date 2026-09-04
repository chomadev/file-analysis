using System.CommandLine;
using System.CommandLine.Parsing;
using FileAnalysis.CLI;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure;
using FileAnalysis.Infrastructure.Analysis;
using FileAnalysis.Infrastructure.Cleanup;
using FileAnalysis.Infrastructure.Data;
using FileAnalysis.Infrastructure.Scanning;
using FileAnalysis.Infrastructure.Survey;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// --- Options ---
var pathOption = new Option<DirectoryInfo?>("--path")
{
    Description = "Directory to scan",
    Required = true,
};

var excludeOption = new Option<string[]>("--exclude")
{
    Description = "Additional glob patterns to exclude (repeatable)",
    AllowMultipleArgumentsPerToken = false,
    Arity = ArgumentArity.ZeroOrMore,
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Enumerate files without writing to the database",
};

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Show each file as it is processed",
};

var skipAiOption = new Option<bool>("--skip-ai")
{
    Description = "Skip Ollama AI analysis and embedding generation (metadata + content extraction only)",
};

var reanalyzeOption = new Option<bool>("--reanalyze")
{
    Description = "Force AI re-analysis even for unchanged files that already have an analysis",
};

var rehashOption = new Option<bool>("--rehash")
{
    Description = "Always compute and compare MD5 for every file, even when size and modified time match the index (slower; catches content edits that preserve mtime)",
};

var skipSurveyOption = new Option<bool>("--skip-survey")
{
    Description = "Skip the directory structure survey; fall back to the static --exclude patterns only",
};

var autoConfirmOption = new Option<bool>("--auto-confirm")
{
    Description = "Non-interactive: accept the AI's suggested classification for undecided directories instead of prompting",
};

var surveyOnlyOption = new Option<bool>("--survey-only")
{
    Description = "Run the directory survey (and confirmation) and stop — don't index any files",
};

// --- Scan command ---
var scanCommand = new Command("scan", "Scan a directory and index file metadata");
scanCommand.Add(pathOption);
scanCommand.Add(excludeOption);
scanCommand.Add(dryRunOption);
scanCommand.Add(verboseOption);
scanCommand.Add(skipAiOption);
scanCommand.Add(reanalyzeOption);
scanCommand.Add(rehashOption);
scanCommand.Add(skipSurveyOption);
scanCommand.Add(autoConfirmOption);
scanCommand.Add(surveyOnlyOption);

scanCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var path         = parseResult.GetValue(pathOption);
    var extraExcludes = parseResult.GetValue(excludeOption) ?? [];
    var dryRun       = parseResult.GetValue(dryRunOption);
    var verbose      = parseResult.GetValue(verboseOption);
    var skipAi       = parseResult.GetValue(skipAiOption);
    var reanalyze    = parseResult.GetValue(reanalyzeOption);
    var rehash       = parseResult.GetValue(rehashOption);
    var skipSurvey   = parseResult.GetValue(skipSurveyOption);
    var autoConfirm  = parseResult.GetValue(autoConfirmOption);
    var surveyOnly   = parseResult.GetValue(surveyOnlyOption);

    if (path is null || !path.Exists)
    {
        Console.Error.WriteLine("Error: --path must be an existing directory.");
        return;
    }

    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var services = new ServiceCollection();

    services.AddFileAnalysisInfrastructure(config);
    services.AddSingleton<IScanProgressReporter>(new ConsoleScanProgressReporter(verbose));

    // Merge any CLI-supplied extra exclude patterns on top of appsettings
    if (extraExcludes.Length > 0)
    {
        services.PostConfigure<FileAnalysis.Core.Options.ScannerOptions>(opts =>
            opts.ExcludePatterns = [.. opts.ExcludePatterns, .. extraExcludes]);
    }

    await using var sp = services.BuildServiceProvider();

    Console.WriteLine($"Scanning {path.FullName}...");
    if (dryRun) Console.WriteLine("  (dry run — no changes will be written)");

    // Apply pending migrations automatically on real runs
    if (!dryRun)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileAnalysisDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    IReadOnlySet<string>? excludedPaths = null;
    if (!dryRun && !skipSurvey)
    {
        using var surveyScope = sp.CreateScope();
        excludedPaths = await RunDirectorySurveyAsync(surveyScope.ServiceProvider, path.FullName, autoConfirm, ct);
    }

    if (surveyOnly)
        return;

    var queue = sp.GetRequiredService<AnalysisQueue>();
    var processingTask = (dryRun || skipAi)
        ? Task.CompletedTask
        : sp.GetRequiredService<AnalysisQueueProcessor>().RunAsync(ct);

    using var scanScope = sp.CreateScope();
    var scanner = scanScope.ServiceProvider.GetRequiredService<FileScanner>();
    await scanner.ScanAsync(path.FullName, dryRun, skipAi, reanalyze, excludedPaths, rehash, ct);

    queue.Writer.TryComplete();
    await processingTask;
});

// --- Search command ---
var queryArgument = new Argument<string>("query") { Description = "Search query text" };
var limitOption = new Option<int>("--limit") { Description = "Max results", DefaultValueFactory = _ => 10 };
var typeFilterOption = new Option<string?>("--type") { Description = "Filter by file extension, e.g. pdf or .pdf" };
var tagFilterOption = new Option<string?>("--tag") { Description = "Filter by tag" };
var categoryFilterOption = new Option<string?>("--category") { Description = "Filter by category" };
var sinceFilterOption = new Option<DateTime?>("--since") { Description = "Only files modified since this date" };

var searchCommand = new Command("search", "Search the index (hybrid full-text + semantic)");
searchCommand.Add(queryArgument);
searchCommand.Add(limitOption);
searchCommand.Add(typeFilterOption);
searchCommand.Add(tagFilterOption);
searchCommand.Add(categoryFilterOption);
searchCommand.Add(sinceFilterOption);

searchCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var query = parseResult.GetValue(queryArgument)!;
    var limit = parseResult.GetValue(limitOption);
    var filters = new SearchFilters
    {
        Extension = NormalizeExtension(parseResult.GetValue(typeFilterOption)),
        Tag = parseResult.GetValue(tagFilterOption),
        Category = parseResult.GetValue(categoryFilterOption),
        Since = parseResult.GetValue(sinceFilterOption),
    };

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var searchService = scope.ServiceProvider.GetRequiredService<SearchService>();
    var results = await searchService.SearchAsync(query, limit, filters, ct);

    PrintResults(results);
});

// --- Show command ---
var fileIdArgument = new Argument<Guid>("file-id") { Description = "File ID, as printed by search/similar" };

var showCommand = new Command("show", "Show full analysis detail for a file");
showCommand.Add(fileIdArgument);

showCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var fileId = parseResult.GetValue(fileIdArgument);

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();
    var detail = await repo.GetDetailAsync(fileId, ct);
    if (detail is null)
    {
        Console.Error.WriteLine("File not found.");
        return;
    }

    var f = detail.File;
    Console.WriteLine(f.Name);
    Console.WriteLine($"  Path:       {f.Path}");
    Console.WriteLine($"  Size:       {f.SizeBytes:N0} bytes");
    Console.WriteLine($"  Extension:  {f.Extension}   MIME: {f.MimeType}");
    Console.WriteLine($"  Indexed:    {f.IndexedAt:u}   Last scanned: {f.LastScannedAt:u}");

    if (detail.Analysis is { } a)
    {
        Console.WriteLine();
        Console.WriteLine($"  Category:    {a.Category}");
        Console.WriteLine($"  Language:    {a.Language}");
        Console.WriteLine($"  Usefulness:  {a.UsefulnessScore}/10");
        Console.WriteLine($"  Duplicate?:  {a.IsDuplicateCandidate}");
        Console.WriteLine($"  Tags:        {string.Join(", ", a.Tags)}");
        Console.WriteLine($"  Topics:      {string.Join(", ", a.Topics)}");
        Console.WriteLine($"  Summary:     {a.Summary}");
        Console.WriteLine($"  Embedding:   {(a.Embedding is null ? "none" : $"{a.Embedding.Length} dims")}");
        Console.WriteLine($"  Analyzed at: {a.AnalyzedAt:u}");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("  (not yet analyzed)");
    }

    Console.WriteLine();
    Console.WriteLine($"  Content chunks: {detail.Contents.Count}");
});

// --- Similar command ---
var similarCommand = new Command("similar", "Find files semantically similar to a given file");
similarCommand.Add(fileIdArgument);
similarCommand.Add(limitOption);

similarCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var fileId = parseResult.GetValue(fileIdArgument);
    var limit = parseResult.GetValue(limitOption);

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();
    var results = await repo.FindSimilarAsync(fileId, limit, ct);

    if (results.Count == 0)
        Console.WriteLine("No similar files found (file may not have an embedding yet).");
    else
        PrintResults(results);
});

// --- Stats command ---
var statsCommand = new Command("stats", "Show index summary statistics");

statsCommand.SetAction(async (ParseResult _, CancellationToken ct) =>
{
    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();
    var stats = await repo.GetStatsAsync(ct);

    Console.WriteLine($"Total files:      {stats.TotalFiles:N0}");
    Console.WriteLine($"Total size:       {stats.TotalSizeBytes / 1024.0 / 1024.0:N1} MB");
    Console.WriteLine($"Analyzed:         {stats.AnalyzedFiles:N0}");
    Console.WriteLine($"Pending analysis: {stats.PendingAnalysis:N0}");

    if (stats.ByExtension.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("By extension:");
        foreach (var kv in stats.ByExtension.OrderByDescending(kv => kv.Value).Take(20))
            Console.WriteLine($"  {(string.IsNullOrEmpty(kv.Key) ? "(none)" : kv.Key),-14} {kv.Value,6}");
    }

    if (stats.ByCategory.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("By category:");
        foreach (var kv in stats.ByCategory.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key,-14} {kv.Value,6}");
    }
});

// --- Chat command ---
var mcpDllOption = new Option<string>("--mcp-dll")
{
    Description = "Path to the built FileAnalysis.MCP.dll",
    DefaultValueFactory = _ => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "FileAnalysis.MCP", "bin", "Release", "net8.0", "publish", "FileAnalysis.MCP.dll"),
};
var modelOption = new Option<string>("--model") { Description = "Ollama model to chat with", DefaultValueFactory = _ => "qwen3:14b" };

var chatCommand = new Command("chat", "Interactive terminal chat with an Ollama model wired to the file-analysis MCP tools (no Open WebUI needed)");
chatCommand.Add(mcpDllOption);
chatCommand.Add(modelOption);

chatCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var mcpDllPath = Path.GetFullPath(parseResult.GetValue(mcpDllOption)!);
    var model = parseResult.GetValue(modelOption)!;

    if (!File.Exists(mcpDllPath))
    {
        Console.Error.WriteLine($"Error: MCP server DLL not found at {mcpDllPath}. Build it first: dotnet publish src/FileAnalysis.MCP -c Release");
        return;
    }

    await ChatSession.RunAsync(mcpDllPath, "http://localhost:11434", model, ct);
});

// --- Cleanup commands ---
var cleanupPathOption = new Option<string?>("--path") { Description = "Only consider/list files under this path prefix" };
var staleDaysOption = new Option<int?>("--older-than") { Description = "Override the configured stale-days threshold for this run" };
var minUsefulnessOverrideOption = new Option<int?>("--min-usefulness") { Description = "Override the configured low-usefulness threshold for this run" };
var similarityThresholdOption = new Option<double?>("--similarity-threshold") { Description = "Override the configured near-duplicate similarity threshold for this run" };

var suggestCleanupCommand = new Command("suggest-cleanup", "Scan the index for files that may be safe to remove (duplicates, low-usefulness, stale) and record suggestions for review");
suggestCleanupCommand.Add(cleanupPathOption);
suggestCleanupCommand.Add(staleDaysOption);
suggestCleanupCommand.Add(minUsefulnessOverrideOption);
suggestCleanupCommand.Add(similarityThresholdOption);
suggestCleanupCommand.Add(dryRunOption);

suggestCleanupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var path = parseResult.GetValue(cleanupPathOption);
    var staleDays = parseResult.GetValue(staleDaysOption);
    var minUsefulness = parseResult.GetValue(minUsefulnessOverrideOption);
    var similarityThreshold = parseResult.GetValue(similarityThresholdOption);
    var dryRun = parseResult.GetValue(dryRunOption);

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<CleanupSuggestionService>();

    var result = await service.GenerateAsync(path, staleDays, minUsefulness, similarityThreshold, dryRun, ct);

    if (dryRun)
        Console.WriteLine("(dry run — nothing was saved)");
    Console.WriteLine($"{result.Created.Count} suggestion(s) {(dryRun ? "would be created" : "created")}, {result.Skipped} skipped (already decided).");
    foreach (var item in result.Created)
    {
        Console.WriteLine($"  [{item.Reason}] {item.Path}");
        Console.WriteLine($"        {item.Details}");
    }
});

// --- List cleanup command ---
var statusFilterOption = new Option<string?>("--status") { Description = "Filter by status: pending|approved|rejected|applied" };

var listCleanupCommand = new Command("list-cleanup", "List cleanup suggestions");
listCleanupCommand.Add(statusFilterOption);
listCleanupCommand.Add(cleanupPathOption);

listCleanupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var statusStr = parseResult.GetValue(statusFilterOption);
    var path = parseResult.GetValue(cleanupPathOption);

    CleanupStatus? status = null;
    if (statusStr is not null)
    {
        if (!Enum.TryParse(statusStr, ignoreCase: true, out CleanupStatus parsed))
        {
            Console.Error.WriteLine($"Error: unknown status '{statusStr}'. Valid values: pending, approved, rejected, applied.");
            return;
        }
        status = parsed;
    }

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ICleanupSuggestionRepository>();
    var items = await repo.ListAsync(status, path, ct);

    if (items.Count == 0)
    {
        Console.WriteLine("No suggestions.");
        return;
    }

    foreach (var s in items)
    {
        Console.WriteLine($"{s.Id.ToString("N")[..8]}  [{s.Status}] [{s.Reason}]  {s.File?.Path}");
        if (!string.IsNullOrWhiteSpace(s.Details))
            Console.WriteLine($"          {s.Details}");
    }
});

// --- Approve / reject / apply cleanup commands ---
var suggestionIdArgument = new Argument<string?>("id") { Description = "Suggestion ID (or unambiguous prefix)", Arity = ArgumentArity.ZeroOrOne };
var allFlagOption = new Option<bool>("--all") { Description = "Act on all matching pending suggestions (requires --reason or --path to scope it)" };
var reasonFilterOption = new Option<string?>("--reason") { Description = "Scope --all to this reason: exactduplicate|nearduplicate|lowusefulness|stale" };

var approveCleanupCommand = new Command("approve-cleanup", "Approve one or more pending cleanup suggestions");
approveCleanupCommand.Add(suggestionIdArgument);
approveCleanupCommand.Add(allFlagOption);
approveCleanupCommand.Add(reasonFilterOption);
approveCleanupCommand.Add(cleanupPathOption);
approveCleanupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
    await SetSuggestionStatusAsync(parseResult, suggestionIdArgument, allFlagOption, reasonFilterOption, cleanupPathOption, CleanupStatus.Approved, ct));

var rejectCleanupCommand = new Command("reject-cleanup", "Reject one or more pending cleanup suggestions");
rejectCleanupCommand.Add(suggestionIdArgument);
rejectCleanupCommand.Add(allFlagOption);
rejectCleanupCommand.Add(reasonFilterOption);
rejectCleanupCommand.Add(cleanupPathOption);
rejectCleanupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
    await SetSuggestionStatusAsync(parseResult, suggestionIdArgument, allFlagOption, reasonFilterOption, cleanupPathOption, CleanupStatus.Rejected, ct));

var applyCleanupCommand = new Command("apply-cleanup", "Move approved cleanup suggestions' files into quarantine — never deletes");
applyCleanupCommand.Add(suggestionIdArgument);
applyCleanupCommand.Add(allFlagOption);
applyCleanupCommand.Add(reasonFilterOption);
applyCleanupCommand.Add(cleanupPathOption);
applyCleanupCommand.Add(dryRunOption);

applyCleanupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var id = parseResult.GetValue(suggestionIdArgument);
    var all = parseResult.GetValue(allFlagOption);
    var reasonStr = parseResult.GetValue(reasonFilterOption);
    var path = parseResult.GetValue(cleanupPathOption);
    var dryRun = parseResult.GetValue(dryRunOption);

    if (!TryValidateIdOrAllScope(id, all, reasonStr, path, out var reason, out var error))
    {
        Console.Error.WriteLine($"Error: {error}");
        return;
    }

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ICleanupSuggestionRepository>();
    var applier = scope.ServiceProvider.GetRequiredService<CleanupApplier>();

    List<CleanupSuggestion> targets;
    if (all)
    {
        targets = (await repo.ListAsync(CleanupStatus.Approved, path, ct))
            .Where(s => reason is null || s.Reason == reason)
            .ToList();
    }
    else
    {
        CleanupSuggestion? single;
        try
        {
            single = await repo.GetByIdOrPrefixAsync(id!, ct);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return;
        }
        if (single is null)
        {
            Console.Error.WriteLine("Suggestion not found.");
            return;
        }
        targets = [single];
    }

    if (targets.Count == 0)
    {
        Console.WriteLine("Nothing to apply.");
        return;
    }

    foreach (var suggestion in targets)
    {
        var result = await applier.ApplyAsync(suggestion.Id, dryRun, ct);
        if (result.Ok)
            Console.WriteLine($"{(dryRun ? "[dry-run] would move" : "moved")}: {result.SourcePath} -> {result.DestinationPath}");
        else
            Console.Error.WriteLine($"skipped {suggestion.Id.ToString("N")[..8]}: {result.Error}");
    }
});

// --- Root command ---
var rootCommand = new RootCommand("File Analysis Tool — index and search your files");
rootCommand.Add(scanCommand);
rootCommand.Add(searchCommand);
rootCommand.Add(showCommand);
rootCommand.Add(similarCommand);
rootCommand.Add(statsCommand);
rootCommand.Add(chatCommand);
rootCommand.Add(suggestCleanupCommand);
rootCommand.Add(listCleanupCommand);
rootCommand.Add(approveCleanupCommand);
rootCommand.Add(rejectCleanupCommand);
rootCommand.Add(applyCleanupCommand);

return await rootCommand.Parse(args).InvokeAsync();

static async Task<ServiceProvider> BuildServiceProviderAsync(CancellationToken ct)
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var services = new ServiceCollection();
    services.AddFileAnalysisInfrastructure(config);
    // CLI-only: CleanupApplier is the only type that moves files for cleanup, and is deliberately
    // never registered in AddFileAnalysisInfrastructure so the MCP server can't resolve it.
    services.AddScoped<CleanupApplier>();
    var sp = services.BuildServiceProvider();

    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FileAnalysisDbContext>();
    await db.Database.MigrateAsync(ct);

    return sp;
}

static bool TryValidateIdOrAllScope(string? id, bool all, string? reasonStr, string? path, out CleanupReason? reason, out string? error)
{
    reason = null;
    error = null;

    if (!all && id is null)
    {
        error = "Provide a suggestion ID, or use --all with --reason or --path.";
        return false;
    }
    if (all && reasonStr is null && path is null)
    {
        error = "--all requires --reason or --path to scope it — bulk-deciding every pending suggestion isn't allowed by accident.";
        return false;
    }
    if (reasonStr is not null)
    {
        if (!Enum.TryParse<CleanupReason>(reasonStr, ignoreCase: true, out var parsedReason))
        {
            error = $"Unknown reason '{reasonStr}'. Valid values: exactduplicate, nearduplicate, lowusefulness, stale.";
            return false;
        }
        reason = parsedReason;
    }
    return true;
}

static async Task SetSuggestionStatusAsync(
    ParseResult parseResult, Argument<string?> idArg, Option<bool> allOpt, Option<string?> reasonOpt, Option<string?> pathOpt,
    CleanupStatus newStatus, CancellationToken ct)
{
    var id = parseResult.GetValue(idArg);
    var all = parseResult.GetValue(allOpt);
    var reasonStr = parseResult.GetValue(reasonOpt);
    var path = parseResult.GetValue(pathOpt);

    if (!TryValidateIdOrAllScope(id, all, reasonStr, path, out var reason, out var error))
    {
        Console.Error.WriteLine($"Error: {error}");
        return;
    }

    await using var sp = await BuildServiceProviderAsync(ct);
    using var scope = sp.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ICleanupSuggestionRepository>();

    if (all)
    {
        var count = await repo.BulkSetStatusAsync(CleanupStatus.Pending, newStatus, reason, path, ct);
        Console.WriteLine($"{count} suggestion(s) set to {newStatus}.");
        return;
    }

    CleanupSuggestion? suggestion;
    try
    {
        suggestion = await repo.GetByIdOrPrefixAsync(id!, ct);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return;
    }

    if (suggestion is null)
    {
        Console.Error.WriteLine("Suggestion not found.");
        return;
    }
    if (suggestion.Status != CleanupStatus.Pending)
    {
        Console.Error.WriteLine($"Suggestion is already {suggestion.Status}.");
        return;
    }

    suggestion.Status = newStatus;
    suggestion.DecidedAt = DateTime.UtcNow;
    await repo.UpsertAsync(suggestion, ct);
    Console.WriteLine($"Suggestion set to {newStatus}.");
}

static async Task<IReadOnlySet<string>> RunDirectorySurveyAsync(IServiceProvider services, string rootPath, bool autoConfirm, CancellationToken ct)
{
    var surveyor = services.GetRequiredService<DirectorySurveyor>();
    var repository = services.GetRequiredService<IDirectorySurveyRepository>();

    Console.WriteLine("Analisando estrutura de diretórios...");
    var result = await surveyor.SurveyAsync(rootPath, ct);

    if (result.AutoExcluded.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Excluindo automaticamente:");
        foreach (var entry in result.AutoExcluded)
        {
            var reason = entry.ClassificationSource == ClassificationSource.Heuristic ? "nome conhecido" : entry.ClassificationReason;
            Console.WriteLine($"  {entry.Path}  ({entry.TotalFileCount:N0} arquivos, {FormatSize(entry.TotalSizeBytes)}) — {reason}");
        }
    }

    foreach (var entry in result.NeedsConfirmation)
    {
        bool exclude;
        ClassificationSource source;
        if (autoConfirm)
        {
            exclude = entry.Classification == DirectoryClassification.LikelyJunk;
            source = entry.ClassificationSource; // preserve Ai (or None, if classification failed)
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"Diretório '{entry.Path}' ({entry.TotalFileCount:N0} arquivos, {FormatSize(entry.TotalSizeBytes)}) parece ser {DescribeClassification(entry.Classification)} ({entry.ClassificationReason}).");
            Console.Write("Excluir da indexação? [s/N]: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            exclude = input is "s" or "sim" or "y" or "yes";
            source = ClassificationSource.Human;
        }

        await surveyor.ConfirmAsync(entry, exclude, source, ct);
    }

    return await repository.GetExcludedPathsAsync(rootPath, ct);
}

static string DescribeClassification(DirectoryClassification c) => c switch
{
    DirectoryClassification.LikelyJunk => "um artefato de build/dependência",
    DirectoryClassification.LikelyContent => "conteúdo real",
    _ => "incerto",
};

static string FormatSize(long bytes) =>
    bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):F1} GB" :
    bytes >= 1024L * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB" :
    bytes >= 1024L ? $"{bytes / 1024.0:F1} KB" :
    $"{bytes} bytes";

static string? NormalizeExtension(string? type) =>
    string.IsNullOrWhiteSpace(type) ? null : "." + type.TrimStart('.').ToLowerInvariant();

static void PrintResults(IReadOnlyList<SearchResult> results)
{
    if (results.Count == 0)
    {
        Console.WriteLine("No results.");
        return;
    }

    foreach (var r in results)
    {
        Console.WriteLine($"{r.Score:F3}  {r.Name}  [{r.Category ?? "-"}]  {r.FileId}");
        Console.WriteLine($"        {r.Path}");
        if (!string.IsNullOrWhiteSpace(r.Summary))
            Console.WriteLine($"        {r.Summary}");
        if (r.Tags.Length > 0)
            Console.WriteLine($"        tags: {string.Join(", ", r.Tags)}");
        Console.WriteLine();
    }
}
