namespace RoslynRag.Core.Models;

public sealed class SolutionIndexState
{
    public required string SolutionPath { get; set; }
    public string? LastIndexedCommitSha { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
    public int TotalChunks { get; set; }
    public int TotalFiles { get; set; }
    public required string EmbeddingModel { get; set; }
    public int EmbeddingDimensions { get; set; }
    public Dictionary<string, string> FileHashes { get; set; } = new();
}

public sealed class IndexState
{
    public Dictionary<string, SolutionIndexState> Solutions { get; set; } = new();
}
