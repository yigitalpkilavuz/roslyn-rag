using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Storage;

public sealed class RrfSearchFusion : ISearchFusion
{
    private readonly int _k;

    public RrfSearchFusion(int k = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(k, 0);
        _k = k;
    }

    public IReadOnlyList<FusedSearchResult> Fuse(
        IReadOnlyList<SearchResult> vectorResults,
        IReadOnlyList<SearchResult> bm25Results,
        int topK = 10)
    {
        ArgumentNullException.ThrowIfNull(vectorResults);
        ArgumentNullException.ThrowIfNull(bm25Results);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(topK, 0);
        var scores = new Dictionary<string, (float rrfScore, float? vectorScore, float? bm25Score, SearchResult result)>(vectorResults.Count + bm25Results.Count);

        for (var rank = 0; rank < vectorResults.Count; rank++)
        {
            var r = vectorResults[rank];
            var rrfScore = 1.0f / (_k + rank + 1);

            scores[r.ChunkId] = (rrfScore, r.Score, null, r);
        }

        for (var rank = 0; rank < bm25Results.Count; rank++)
        {
            var r = bm25Results[rank];
            var rrfScore = 1.0f / (_k + rank + 1);

            if (scores.TryGetValue(r.ChunkId, out var existing))
            {
                scores[r.ChunkId] = (
                    existing.rrfScore + rrfScore,
                    existing.vectorScore,
                    r.Score,
                    existing.result
                );
            }
            else
            {
                scores[r.ChunkId] = (rrfScore, null, r.Score, r);
            }
        }

        return scores.Values
            .OrderByDescending(s => s.rrfScore)
            .Take(topK)
            .Select(s => new FusedSearchResult
            {
                ChunkId = s.result.ChunkId,
                FusedScore = s.rrfScore,
                VectorScore = s.vectorScore,
                Bm25Score = s.bm25Score,
                SolutionId = s.result.SolutionId,
                FilePath = s.result.FilePath,
                ClassName = s.result.ClassName,
                MethodName = s.result.MethodName,
                StartLine = s.result.StartLine,
                EndLine = s.result.EndLine,
                Body = s.result.Body,
                EmbeddingText = s.result.EmbeddingText
            })
            .ToList();
    }
}
