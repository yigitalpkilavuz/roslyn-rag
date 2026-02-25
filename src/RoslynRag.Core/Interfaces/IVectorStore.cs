using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IVectorStore
{
    Task InitializeAsync(int vectorSize, CancellationToken ct = default);

    Task UpsertAsync(
        IReadOnlyList<CodeChunk> chunks,
        IReadOnlyList<EmbeddingResult> embeddings,
        CancellationToken ct = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 20,
        string? solutionId = null,
        CancellationToken ct = default);

    Task DeleteByFilePathsAsync(
        string solutionId,
        IReadOnlySet<string> filePaths,
        CancellationToken ct = default);

    Task DeleteBySolutionIdAsync(
        string solutionId,
        CancellationToken ct = default);

    Task DeleteCollectionAsync(CancellationToken ct = default);

    Task<ulong> GetPointCountAsync(CancellationToken ct = default);
}
