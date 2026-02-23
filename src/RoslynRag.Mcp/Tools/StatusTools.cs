using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RoslynRag.Core.Interfaces;

namespace RoslynRag.Mcp.Tools;

[McpServerToolType]
public static class StatusTools
{
    [McpServerTool(Name = "get_index_status", Destructive = false), Description(
        "Get the current index status: solution path, last indexed commit, chunk/file counts, " +
        "embedding model, and Qdrant connection health.")]
    public static async Task<string> GetIndexStatus(
        IIndexStateStore stateStore,
        IVectorStore vectorStore,
        CancellationToken ct = default)
    {
        var state = await stateStore.LoadAsync(ct).ConfigureAwait(false);

        if (state is null)
            return "No index found. Run index_solution first.";

        var sb = new StringBuilder();
        sb.AppendLine($"Solution: {state.SolutionPath}");
        sb.AppendLine($"Last Commit: {state.LastIndexedCommitSha ?? "N/A"}");
        sb.AppendLine($"Indexed At: {state.IndexedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Total Chunks: {state.TotalChunks:N0}");
        sb.AppendLine($"Total Files: {state.TotalFiles:N0}");
        sb.AppendLine($"Embedding Model: {state.EmbeddingModel}");
        sb.AppendLine($"Dimensions: {state.EmbeddingDimensions}");

        try
        {
            var pointCount = await vectorStore.GetPointCountAsync(ct).ConfigureAwait(false);
            sb.AppendLine($"Qdrant: Connected ({pointCount:N0} points)");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Qdrant: Error — {ex.Message}");
        }

        return sb.ToString();
    }
}
