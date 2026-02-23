namespace RoslynRag.Core.Models;

public sealed record FusedSearchResult
{
    public required string ChunkId { get; init; }
    public required float FusedScore { get; init; }
    public required float? VectorScore { get; init; }
    public required float? Bm25Score { get; init; }
    public required string FilePath { get; init; }
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? Body { get; init; }
    public string? EmbeddingText { get; init; }
}
