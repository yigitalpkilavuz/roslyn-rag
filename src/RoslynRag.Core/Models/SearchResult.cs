namespace RoslynRag.Core.Models;

public sealed record SearchResult
{
    public required string ChunkId { get; init; }
    public required float Score { get; init; }
    public required string SolutionId { get; init; }
    public required string FilePath { get; init; }
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? Body { get; init; }
    public string? EmbeddingText { get; init; }
}
