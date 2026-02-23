namespace RoslynRag.Core.Models;

public sealed record EmbeddingResult
{
    public required string ChunkId { get; init; }
    public required float[] Vector { get; init; }
}
