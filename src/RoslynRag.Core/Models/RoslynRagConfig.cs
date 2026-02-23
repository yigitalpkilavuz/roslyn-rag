namespace RoslynRag.Core.Models;

public sealed class RoslynRagConfig
{
    public QdrantConfig Qdrant { get; init; } = new();
    public OllamaConfig Ollama { get; init; } = new();
    public IndexingConfig Indexing { get; init; } = new();
    public SearchConfig Search { get; init; } = new();
}

public sealed class QdrantConfig
{
    public string Host { get; init; } = "localhost";
    public int GrpcPort { get; init; } = 6334;
    public int RestPort { get; init; } = 6333;
}

public sealed class OllamaConfig
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public int EmbeddingDimensions { get; init; } = 768;
    public string LlmModel { get; init; } = "llama3:8b";
    public int BatchSize { get; init; } = 32;
}

public sealed class IndexingConfig
{
    public string DataDirectory { get; init; } = ".roslyn-rag";
    public int MaxChunkChars { get; init; } = 4000;
}

public sealed class SearchConfig
{
    public int RrfK { get; init; } = 60;
}
