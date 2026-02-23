using System.Security.Cryptography;
using System.Text;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace RoslynRag.Storage;

public sealed class QdrantVectorStore : IVectorStore, IDisposable
{
    private const string CollectionName = "roslyn_rag_chunks";
    private const int UpsertBatchSize = 100;
    private const int MaxDeleteParallelism = 8;

    private readonly QdrantClient _client;

    public QdrantVectorStore(string host = "localhost", int port = 6334)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, 0);
        _client = new QdrantClient(host, port);
    }

    public void Dispose()
    {
        (_client as IDisposable)?.Dispose();
    }

    public async Task InitializeAsync(int vectorSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(vectorSize, 0);
        var collections = await _client.ListCollectionsAsync(ct).ConfigureAwait(false);
        if (collections.Any(c => c == CollectionName))
            return;

        await _client.CreateCollectionAsync(
            CollectionName,
            new VectorParams
            {
                Size = (ulong)vectorSize,
                Distance = Distance.Cosine
            },
            cancellationToken: ct).ConfigureAwait(false);

        await _client.CreatePayloadIndexAsync(CollectionName, "file_path",
            PayloadSchemaType.Keyword, cancellationToken: ct).ConfigureAwait(false);
        await _client.CreatePayloadIndexAsync(CollectionName, "namespace",
            PayloadSchemaType.Keyword, cancellationToken: ct).ConfigureAwait(false);
        await _client.CreatePayloadIndexAsync(CollectionName, "class_name",
            PayloadSchemaType.Keyword, cancellationToken: ct).ConfigureAwait(false);
        await _client.CreatePayloadIndexAsync(CollectionName, "method_name",
            PayloadSchemaType.Keyword, cancellationToken: ct).ConfigureAwait(false);
        await _client.CreatePayloadIndexAsync(CollectionName, "kind",
            PayloadSchemaType.Keyword, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task UpsertAsync(
        IReadOnlyList<CodeChunk> chunks,
        IReadOnlyList<EmbeddingResult> embeddings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(embeddings);

        var embeddingMap = new Dictionary<string, float[]>(embeddings.Count);
        foreach (var e in embeddings)
            embeddingMap[e.ChunkId] = e.Vector;
        var batch = new List<PointStruct>(UpsertBatchSize);

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!embeddingMap.TryGetValue(chunks[i].Id, out var vector))
                continue;

            batch.Add(BuildPoint(chunks[i], vector));

            if (batch.Count >= UpsertBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                await _client.UpsertAsync(CollectionName, batch, cancellationToken: ct).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _client.UpsertAsync(CollectionName, batch, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 20,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(topK, 0);

        var results = await _client.SearchAsync(
            CollectionName,
            queryVector,
            limit: (ulong)topK,
            cancellationToken: ct).ConfigureAwait(false);

        return results.Select(r =>
        {
            var payload = r.Payload;
            return new SearchResult
            {
                ChunkId = r.Id.Uuid,
                Score = r.Score,
                FilePath = GetPayloadString(payload, "file_path"),
                ClassName = GetPayloadString(payload, "class_name"),
                MethodName = GetPayloadString(payload, "method_name"),
                StartLine = GetPayloadInt(payload, "start_line"),
                EndLine = GetPayloadInt(payload, "end_line"),
                Body = GetPayloadStringOrNull(payload, "body"),
                EmbeddingText = GetPayloadStringOrNull(payload, "embedding_text")
            };
        }).ToList();
    }

    public async Task DeleteByFilePathsAsync(
        IReadOnlySet<string> filePaths,
        CancellationToken ct = default)
    {
        await Parallel.ForEachAsync(filePaths,
            new ParallelOptions { MaxDegreeOfParallelism = MaxDeleteParallelism, CancellationToken = ct },
            async (filePath, token) =>
            {
                await _client.DeleteAsync(
                    CollectionName,
                    new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "file_path",
                                    Match = new Match { Keyword = filePath }
                                }
                            }
                        }
                    },
                    cancellationToken: token).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    public async Task DeleteCollectionAsync(CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct).ConfigureAwait(false);
        if (collections.Any(c => c == CollectionName))
        {
            await _client.DeleteCollectionAsync(CollectionName, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task<ulong> GetPointCountAsync(CancellationToken ct = default)
    {
        var info = await _client.GetCollectionInfoAsync(CollectionName, cancellationToken: ct).ConfigureAwait(false);
        return info.PointsCount;
    }

    private static PointStruct BuildPoint(CodeChunk c, float[] vector) => new()
    {
        Id = new PointId { Uuid = ToUuid(c.Id) },
        Vectors = vector,
        Payload =
        {
            ["file_path"] = c.FilePath,
            ["namespace"] = c.Namespace,
            ["class_name"] = c.ClassName,
            ["method_name"] = c.MethodName,
            ["full_signature"] = c.FullSignature,
            ["kind"] = c.Kind.ToString(),
            ["start_line"] = c.StartLine,
            ["end_line"] = c.EndLine,
            ["body"] = c.Body,
            ["embedding_text"] = c.EmbeddingText,
            ["attributes"] = string.Join(", ", c.Attributes),
            ["dependencies"] = string.Join(", ", c.Dependencies),
            ["base_types"] = string.Join(", ", c.BaseTypes)
        }
    };

    private static string ToUuid(string chunkId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chunkId));
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // Version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant 1
        return new Guid(guidBytes).ToString();
    }

    private static string GetPayloadString(
        IDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;

    private static string? GetPayloadStringOrNull(
        IDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : null;

    private static int GetPayloadInt(
        IDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;
}
