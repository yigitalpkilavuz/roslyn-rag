using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IParsePipeline
{
    Task<IReadOnlyList<CodeChunk>> ParseSolutionAsync(
        string solutionPath,
        IProgress<(int processed, int total, string currentFile)>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<CodeChunk>> ParseFilesAsync(
        string solutionPath,
        IReadOnlySet<string> relativeFilePaths,
        CancellationToken ct = default);
}
