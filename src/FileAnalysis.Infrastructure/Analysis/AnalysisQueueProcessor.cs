using FileAnalysis.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Analysis;

/// <summary>Drains the analysis queue with a bounded number of concurrent workers, each in its own DI scope.</summary>
public class AnalysisQueueProcessor(IServiceScopeFactory scopeFactory, AnalysisQueue queue, IOptions<OllamaOptions> options)
{
    public Task RunAsync(CancellationToken ct = default)
    {
        var degree = Math.Max(1, options.Value.MaxConcurrency);
        var workers = Enumerable.Range(0, degree).Select(_ => WorkerLoopAsync(ct));
        return Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var fileId in queue.Reader.ReadAllAsync(ct))
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AnalysisService>();
            try
            {
                await service.AnalyzeAsync(fileId, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[analysis] {fileId}: unexpected failure — {ex.Message}");
            }
        }
    }
}
