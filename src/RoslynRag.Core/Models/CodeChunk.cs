namespace RoslynRag.Core.Models;

public enum ChunkKind
{
    Method,
    Constructor,
    ClassHeader
}

public sealed record CodeChunk
{
    public required string Id { get; init; }
    public required string FilePath { get; init; }
    public required string Namespace { get; init; }
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required string FullSignature { get; init; }
    public required ChunkKind Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Body { get; init; }
    public required string[] Attributes { get; init; }
    public required string[] Dependencies { get; init; }
    public required string[] BaseTypes { get; init; }
    public required string EmbeddingText { get; init; }
    public int? PartIndex { get; init; }
    public int? TotalParts { get; init; }
}
