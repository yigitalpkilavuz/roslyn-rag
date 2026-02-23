using System.Net.Http.Json;
using System.Text.Json.Serialization;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Indexing;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly int _batchSize;

    public int Dimensions { get; }
    public string ModelName { get; }

    public OllamaEmbeddingService(
        HttpClient httpClient,
        string modelName = "jina/jina-embeddings-v3",
        int dimensions = 768,
        int batchSize = 32)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimensions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);
        _httpClient = httpClient;
        ModelName = modelName;
        Dimensions = dimensions;
        _batchSize = batchSize;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var request = new OllamaEmbedRequest
        {
            Model = ModelName,
            Input = [text]
        };

        var response = await _httpClient.PostAsJsonAsync("/api/embed", request, OllamaJsonContext.Default.OllamaEmbedRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(OllamaJsonContext.Default.OllamaEmbedResponse, ct).ConfigureAwait(false);
        return result?.Embeddings is [var first, ..]
            ? first
            : throw new InvalidOperationException("Empty embedding response from Ollama");
    }

    public async Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(
        IReadOnlyList<CodeChunk> chunks,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0) return [];

        var results = new List<EmbeddingResult>(chunks.Count);
        var total = chunks.Count;

        for (var i = 0; i < total; i += _batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchEnd = Math.Min(i + _batchSize, total);
            var batch = chunks.Skip(i).Take(batchEnd - i).ToList();
            var texts = batch.ConvertAll(c => c.EmbeddingText);

            var request = new OllamaEmbedRequest
            {
                Model = ModelName,
                Input = texts
            };

            var response = await _httpClient.PostAsJsonAsync("/api/embed", request, OllamaJsonContext.Default.OllamaEmbedRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(OllamaJsonContext.Default.OllamaEmbedResponse, ct).ConfigureAwait(false);
            if (result?.Embeddings is null || result.Embeddings.Count != batch.Count)
                throw new InvalidOperationException(
                    $"Embedding response mismatch: expected {batch.Count} embeddings, got {result?.Embeddings?.Count ?? 0}");

            for (var j = 0; j < batch.Count; j++)
            {
                results.Add(new EmbeddingResult
                {
                    ChunkId = batch[j].Id,
                    Vector = result.Embeddings[j]
                });
            }

            progress?.Report((batchEnd, total));
        }

        return results;
    }
}

[JsonSerializable(typeof(OllamaEmbedRequest))]
[JsonSerializable(typeof(OllamaEmbedResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext;

internal sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required List<string> Input { get; init; }
}

internal sealed class OllamaEmbedResponse
{
    [JsonPropertyName("embeddings")]
    public List<float[]>? Embeddings { get; init; }
}
