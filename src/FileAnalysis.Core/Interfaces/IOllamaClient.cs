namespace FileAnalysis.Core.Interfaces;

/// <summary>Client for the local Ollama HTTP API.</summary>
public interface IOllamaClient
{
    /// <summary>Runs a text/chat completion and returns the raw model output.</summary>
    Task<string> GenerateAsync(string model, string prompt, CancellationToken ct = default);

    /// <summary>Generates an embedding vector for the given text.</summary>
    Task<float[]> EmbedAsync(string model, string text, CancellationToken ct = default);

    /// <summary>Describes an image using a vision-capable model (e.g. llava).</summary>
    Task<string> DescribeImageAsync(string model, byte[] imageBytes, string prompt, CancellationToken ct = default);
}
