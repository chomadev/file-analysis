using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Options;
using FileAnalysis.Infrastructure.Analysis;
using FileAnalysis.Infrastructure.Cleanup;
using FileAnalysis.Infrastructure.Data;
using FileAnalysis.Infrastructure.Extraction;
using FileAnalysis.Infrastructure.Ollama;
using FileAnalysis.Infrastructure.Scanning;
using FileAnalysis.Infrastructure.Survey;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure;

/// <summary>Registers all Infrastructure services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFileAnalysisInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<FileAnalysisDbContext>(o =>
            o.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.UseVector()));

        services.Configure<ScannerOptions>(opts =>
            configuration.GetSection(ScannerOptions.SectionName).Bind(opts));
        services.Configure<ExtractionOptions>(opts =>
            configuration.GetSection(ExtractionOptions.SectionName).Bind(opts));
        services.Configure<OllamaOptions>(opts =>
            configuration.GetSection(OllamaOptions.SectionName).Bind(opts));
        services.Configure<DirectorySurveyOptions>(opts =>
            configuration.GetSection(DirectorySurveyOptions.SectionName).Bind(opts));
        services.Configure<CleanupOptions>(opts =>
            configuration.GetSection(CleanupOptions.SectionName).Bind(opts));

        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFileContentRepository, FileContentRepository>();
        services.AddScoped<IFileAnalysisRepository, FileAnalysisRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IDirectorySurveyRepository, DirectorySurveyRepository>();
        services.AddScoped<ICleanupSuggestionRepository, CleanupSuggestionRepository>();

        // Extractors — register all, dispatcher resolves by MIME type
        services.AddSingleton<IContentExtractor, PlainTextExtractor>();
        services.AddSingleton<IContentExtractor, CodeFileExtractor>();
        services.AddSingleton<IContentExtractor, PdfExtractor>();
        services.AddSingleton<IContentExtractor, OfficeExtractor>();
        services.AddSingleton<IContentExtractor, ThreeMfExtractor>();
        services.AddSingleton<ContentExtractorDispatcher>();
        services.AddSingleton<ContentChunker>();

        services.AddScoped<FileScanner>();

        services.AddHttpClient<IOllamaClient, OllamaClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<AnalysisService>();
        services.AddScoped<SearchService>();
        services.AddScoped<DirectorySurveyor>();
        services.AddScoped<CleanupSuggestionService>();
        // CleanupApplier (the only type that moves files for cleanup) is deliberately NOT registered here —
        // it's added only by the CLI's own composition root, so the MCP server can never resolve it.
        services.AddSingleton<AnalysisQueue>();
        services.AddSingleton<AnalysisQueueProcessor>();

        return services;
    }
}
