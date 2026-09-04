using System.Net.Http.Json;
using System.Text.Json;
using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.Infrastructure.Ollama;

/// <summary>Typed HTTP client for the local Ollama server.</summary>
public class OllamaClient(HttpClient http) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateAsync(string model, string prompt, CancellationToken ct = default)
    {
        // think:false disables reasoning-model "thinking" (qwen3, deepseek-r1, gpt-oss, etc.) — without it,
        // some models spend their generation budget on a <think> block and emit an empty "{}" for format:json.
        var request = new GenerateRequest(model, prompt, false, "json", false);
        using var response = await http.PostAsJsonAsync("api/generate", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOptions, ct);
        return payload?.Response ?? throw new InvalidOperationException("Ollama returned an empty generate response.");
    }

    public async Task<float[]> EmbedAsync(string model, string text, CancellationToken ct = default)
    {
        var request = new EmbedRequest(model, text);
        using var response = await http.PostAsJsonAsync("api/embeddings", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbedResponse>(JsonOptions, ct);
        return payload?.Embedding ?? throw new InvalidOperationException("Ollama returned no embedding.");
    }

    public async Task<string> DescribeImageAsync(string model, byte[] imageBytes, string prompt, CancellationToken ct = default)
    {
        var request = new VisionRequest(model, prompt, false, [Convert.ToBase64String(imageBytes)]);
        using var response = await http.PostAsJsonAsync("api/generate", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOptions, ct);
        return payload?.Response ?? throw new InvalidOperationException("Ollama returned an empty vision response.");
    }

    private sealed record GenerateRequest(string Model, string Prompt, bool Stream, string Format, bool Think);
    private sealed record VisionRequest(string Model, string Prompt, bool Stream, string[] Images);
    private sealed record GenerateResponse(string? Response);
    private sealed record EmbedRequest(string Model, string Prompt);
    private sealed record EmbedResponse(float[]? Embedding);
}
