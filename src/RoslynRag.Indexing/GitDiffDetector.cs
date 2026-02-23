using System.Diagnostics;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Indexing;

public sealed class GitDiffDetector : IGitDiffDetector
{
    public async Task<string> GetCurrentCommitShaAsync(string repoPath, CancellationToken ct = default)
    {
        var output = await RunGitAsync(repoPath, "rev-parse HEAD", ct).ConfigureAwait(false);
        return output.Trim();
    }

    public async Task<GitDiffResult> GetChangedFilesAsync(
        string repoPath, string fromCommitSha, string toCommitSha, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromCommitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(toCommitSha);

        var output = await RunGitAsync(repoPath,
            $"diff --name-status {fromCommitSha} {toCommitSha} -- *.cs", ct).ConfigureAwait(false);

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length < 2) continue;

            var status = parts[0].Trim();
            if (status.Length == 0) continue;

            var filePath = parts[1].Trim().Replace('\\', '/');

            switch (status[0])
            {
                case 'A':
                    added.Add(filePath);
                    break;
                case 'M':
                    modified.Add(filePath);
                    break;
                case 'D':
                    deleted.Add(filePath);
                    break;
                case 'R':
                    var renameParts = filePath.Split('\t', 2);
                    if (renameParts.Length == 2)
                    {
                        deleted.Add(renameParts[0].Replace('\\', '/'));
                        added.Add(renameParts[1].Replace('\\', '/'));
                    }
                    break;
                case 'C':
                    added.Add(filePath);
                    break;
                default:
                    break;
            }
        }

        return new GitDiffResult
        {
            AddedFiles = added,
            ModifiedFiles = modified,
            DeletedFiles = deleted
        };
    }

    public async Task<bool> HasUncommittedChangesAsync(string repoPath, CancellationToken ct = default)
    {
        var output = await RunGitAsync(repoPath, "status --porcelain -- *.cs", ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static async Task<string> RunGitAsync(string workingDirectory, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        try
        {
            // Read stdout and stderr concurrently to avoid deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var error = await errorTask.ConfigureAwait(false);
                throw new InvalidOperationException($"git {arguments} failed (exit {process.ExitCode}): {error}");
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
    }
}
