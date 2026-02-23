using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynRag.Core.Interfaces;
using RoslynRag.Indexing;

namespace RoslynRag.Mcp.Tools;

[McpServerToolType]
public static class IndexTools
{
    [McpServerTool(Name = "index_solution"), Description(
        "Index a .NET solution for code search. Parses C# files with Roslyn, generates embeddings, " +
        "and stores them in Qdrant + Lucene. Supports incremental indexing via git diff.")]
    public static async Task<string> IndexSolution(
        IParsePipeline parser,
        IEmbeddingService embedding,
        IVectorStore vectorStore,
        IKeywordIndex keywordIndex,
        IIndexStateStore stateStore,
        IGitDiffDetector gitDiff,
        [Description("Absolute path to the .sln or .slnx file")] string solutionPath,
        [Description("Force full re-index, ignoring incremental diff (default: false)")] bool forceFullIndex = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(solutionPath))
            return $"Solution file not found: {solutionPath}";

        var pipeline = new IndexingPipeline(
            parser, embedding, vectorStore, keywordIndex, stateStore, gitDiff);

        var messages = new ConcurrentQueue<string>();
        var statusProgress = new Progress<string>(msg => messages.Enqueue(msg));

        await pipeline.IndexAsync(
            solutionPath,
            forceFullIndex: forceFullIndex,
            statusProgress: statusProgress,
            ct: ct).ConfigureAwait(false);

        messages.Enqueue("Indexing complete.");
        return string.Join('\n', messages);
    }
}
