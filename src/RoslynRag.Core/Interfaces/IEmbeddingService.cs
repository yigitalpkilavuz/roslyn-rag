using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IEmbeddingService
{
    int Dimensions { get; }
    string ModelName { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(
        IReadOnlyList<CodeChunk> chunks,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default);
}
