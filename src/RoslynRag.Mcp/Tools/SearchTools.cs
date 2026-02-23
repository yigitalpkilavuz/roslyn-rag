using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RoslynRag.Core.Interfaces;

namespace RoslynRag.Mcp.Tools;

[McpServerToolType]
public static class SearchTools
{
    [McpServerTool(Name = "search_code", Destructive = false), Description(
        "Search indexed .NET codebase using hybrid vector + keyword search. " +
        "Returns ranked code chunks with file paths, line numbers, and source code. " +
        "Use natural language queries (\"how does authentication work\") or exact identifiers (\"ParseSolutionAsync\").")]
    public static async Task<string> SearchCode(
        IEmbeddingService embedding,
        IVectorStore vectorStore,
        IKeywordIndex keywordIndex,
        ISearchFusion fusion,
        [Description("Natural language query or code identifier to search for")] string query,
        [Description("Number of results to return (default: 10)")] int topK = 10,
        CancellationToken ct = default)
    {
        keywordIndex.Initialize();

        var queryVector = await embedding.EmbedAsync(query, ct).ConfigureAwait(false);

        var vectorTask = vectorStore.SearchAsync(queryVector, topK * 2, ct);
        var bm25Task = Task.Run(() => keywordIndex.Search(query, topK * 2), ct);
        await Task.WhenAll(vectorTask, bm25Task).ConfigureAwait(false);

        var results = fusion.Fuse(await vectorTask, await bm25Task, topK);

        if (results.Count == 0)
            return "No results found.";

        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var label = !string.IsNullOrEmpty(r.MethodName)
                ? $"{r.ClassName}.{r.MethodName}"
                : r.ClassName;

            sb.AppendLine($"### [{i + 1}] {label}");
            sb.AppendLine($"**File:** {r.FilePath}:{r.StartLine}-{r.EndLine}");
            sb.AppendLine($"**Score:** {r.FusedScore:F4} (vector: {r.VectorScore?.ToString("F4") ?? "—"}, bm25: {r.Bm25Score?.ToString("F4") ?? "—"})");

            if (r.Body is not null)
            {
                sb.AppendLine("```csharp");
                sb.AppendLine(r.Body);
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
