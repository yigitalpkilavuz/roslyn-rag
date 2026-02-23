using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IGitDiffDetector
{
    Task<string> GetCurrentCommitShaAsync(
        string repoPath, CancellationToken ct = default);

    Task<GitDiffResult> GetChangedFilesAsync(
        string repoPath,
        string fromCommitSha,
        string toCommitSha,
        CancellationToken ct = default);

    Task<bool> HasUncommittedChangesAsync(
        string repoPath, CancellationToken ct = default);
}
