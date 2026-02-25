using System.Text;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Query;

public sealed class QueryPipeline
{
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordIndex _keywordIndex;
    private readonly ISearchFusion _fusion;
    private readonly ILlmService _llm;

    public QueryPipeline(
        IEmbeddingService embedding,
        IVectorStore vectorStore,
        IKeywordIndex keywordIndex,
        ISearchFusion fusion,
        ILlmService llm)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(vectorStore);
        ArgumentNullException.ThrowIfNull(keywordIndex);
        ArgumentNullException.ThrowIfNull(fusion);
        ArgumentNullException.ThrowIfNull(llm);

        _embedding = embedding;
        _vectorStore = vectorStore;
        _keywordIndex = keywordIndex;
        _fusion = fusion;
        _llm = llm;
    }

    public async Task<QueryResult> QueryAsync(
        string question,
        int topK = 10,
        bool useLlm = true,
        string? solutionId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(topK, 0);

        var queryVector = await _embedding.EmbedAsync(question, ct).ConfigureAwait(false);

        _keywordIndex.Initialize();

        var vectorTask = _vectorStore.SearchAsync(queryVector, topK * 2, solutionId, ct);
        var bm25Task = Task.Run(() => _keywordIndex.Search(question, topK * 2, solutionId), ct);
        await Task.WhenAll(vectorTask, bm25Task).ConfigureAwait(false);
        var vectorResults = await vectorTask.ConfigureAwait(false);
        var bm25Results = await bm25Task.ConfigureAwait(false);

        var fusedResults = _fusion.Fuse(vectorResults, bm25Results, topK);

        string? answer = null;
        if (useLlm && fusedResults.Count > 0)
        {
            var prompt = BuildPrompt(question, fusedResults);
            answer = await _llm.GenerateAsync(prompt, ct).ConfigureAwait(false);
        }

        return new QueryResult
        {
            Question = question,
            Answer = answer,
            Sources = fusedResults
        };
    }

    private static string BuildPrompt(string question, IReadOnlyList<FusedSearchResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a code intelligence assistant. Answer the developer's question using ONLY the code context provided below. Always cite your sources with file path and line numbers.");
        sb.AppendLine();
        sb.AppendLine("## Code Context");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var label = !string.IsNullOrEmpty(r.MethodName)
                ? $"{r.ClassName}.{r.MethodName}"
                : r.ClassName;

            sb.AppendLine($"### [{i + 1}] {r.FilePath}:{r.StartLine}-{r.EndLine} ({label})");
            sb.AppendLine("```csharp");
            sb.AppendLine(r.Body ?? r.EmbeddingText ?? "(no source available)");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Question");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("- Answer based ONLY on the code context above");
        sb.AppendLine("- Reference sources as [1], [2], etc.");
        sb.AppendLine("- Include file paths and line numbers in your answer");
        sb.AppendLine("- If the context doesn't contain enough information, say so explicitly");

        return sb.ToString();
    }
}

public sealed record QueryResult
{
    public required string Question { get; init; }
    public string? Answer { get; init; }
    public required IReadOnlyList<FusedSearchResult> Sources { get; init; }
}
