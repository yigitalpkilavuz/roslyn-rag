using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RoslynRag.Core.Interfaces;

namespace RoslynRag.Mcp.Tools;

[McpServerToolType]
public static class StatusTools
{
    [McpServerTool(Name = "get_index_status", Destructive = false), Description(
        "Get the current index status for all indexed solutions: solution paths, last indexed commits, " +
        "chunk/file counts, embedding model, and Qdrant connection health.")]
    public static async Task<string> GetIndexStatus(
        IIndexStateStore stateStore,
        IVectorStore vectorStore,
        CancellationToken ct = default)
    {
        var state = await stateStore.LoadAllAsync(ct).ConfigureAwait(false);

        if (state.Solutions.Count == 0)
            return "No index found. Run index_solution first.";

        var sb = new StringBuilder();

        foreach (var (_, s) in state.Solutions.OrderBy(x => x.Key))
        {
            sb.AppendLine($"## {Path.GetFileName(s.SolutionPath)}");
            sb.AppendLine($"Solution: {s.SolutionPath}");
            sb.AppendLine($"Last Commit: {s.LastIndexedCommitSha ?? "N/A"}");
            sb.AppendLine($"Indexed At: {s.IndexedAt:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine($"Total Chunks: {s.TotalChunks:N0}");
            sb.AppendLine($"Total Files: {s.TotalFiles:N0}");
            sb.AppendLine($"Embedding Model: {s.EmbeddingModel}");
            sb.AppendLine($"Dimensions: {s.EmbeddingDimensions}");
            sb.AppendLine();
        }

        try
        {
            var pointCount = await vectorStore.GetPointCountAsync(ct).ConfigureAwait(false);
            sb.AppendLine($"Qdrant: Connected ({pointCount:N0} points total)");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Qdrant: Error — {ex.Message}");
        }

        return sb.ToString();
    }
}
