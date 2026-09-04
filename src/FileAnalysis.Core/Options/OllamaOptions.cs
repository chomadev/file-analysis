namespace FileAnalysis.Core.Options;

/// <summary>Configuration for the local Ollama server used for AI analysis and embeddings.</summary>
public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string AnalysisModel { get; set; } = "llama3.2:3b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string VisionModel { get; set; } = "llava:7b";

    /// <summary>Number of files analyzed concurrently against Ollama.</summary>
    public int MaxConcurrency { get; set; } = 1;
}
