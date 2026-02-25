using System.Collections.Concurrent;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynRag.Parsing;

public sealed class RoslynParsePipeline : IParsePipeline
{
    private readonly IChunkSplitter _chunkSplitter;
    private static volatile bool _msbuildRegistered;
    private static readonly Lock _registrationLock = new();

    public RoslynParsePipeline(IChunkSplitter chunkSplitter)
    {
        ArgumentNullException.ThrowIfNull(chunkSplitter);
        _chunkSplitter = chunkSplitter;
        EnsureMsBuildRegistered();
    }

    public async Task<IReadOnlyList<CodeChunk>> ParseSolutionAsync(
        string solutionPath,
        IProgress<(int processed, int total, string currentFile)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        var absoluteSolutionPath = Path.GetFullPath(solutionPath);
        var solutionRoot = Path.GetDirectoryName(absoluteSolutionPath)
            ?? throw new ArgumentException("Cannot determine solution directory", nameof(solutionPath));

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"Workspace warning: {e.Diagnostic.Message}");
        };

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);

        var csDocuments = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath is not null
                && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !IsGeneratedFile(d.FilePath))
            .ToList();

        var allChunks = new ConcurrentBag<CodeChunk>();
        var processed = 0;

        await Parallel.ForEachAsync(csDocuments,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (document, token) =>
            {
                var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
                if (root is null) return;

                var walker = new MethodChunkWalker(document.FilePath!, solutionRoot, absoluteSolutionPath);
                walker.Visit(root);

                var chunks = ApplyChunkSplitting(walker.Chunks);
                foreach (var chunk in chunks)
                    allChunks.Add(chunk);

                var p = Interlocked.Increment(ref processed);
                progress?.Report((p, csDocuments.Count, document.FilePath!));
            }).ConfigureAwait(false);

        return DeduplicateById(allChunks);
    }

    public async Task<IReadOnlyList<CodeChunk>> ParseFilesAsync(
        string solutionPath,
        IReadOnlySet<string> relativeFilePaths,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(relativeFilePaths);

        var absoluteSolutionPath = Path.GetFullPath(solutionPath);
        var solutionRoot = Path.GetDirectoryName(absoluteSolutionPath)
            ?? throw new ArgumentException("Cannot determine solution directory", nameof(solutionPath));

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"Workspace warning: {e.Diagnostic.Message}");
        };

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);

        var normalizedPaths = relativeFilePaths
            .Select(p => p.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetDocuments = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d =>
            {
                if (d.FilePath is null) return false;
                var relative = Path.GetRelativePath(solutionRoot, d.FilePath).Replace('\\', '/');
                return normalizedPaths.Contains(relative);
            })
            .ToList();

        var allChunks = new ConcurrentBag<CodeChunk>();

        await Parallel.ForEachAsync(targetDocuments,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (document, token) =>
            {
                var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
                if (root is null) return;

                var walker = new MethodChunkWalker(document.FilePath!, solutionRoot, absoluteSolutionPath);
                walker.Visit(root);

                var chunks = ApplyChunkSplitting(walker.Chunks);
                foreach (var chunk in chunks)
                    allChunks.Add(chunk);
            }).ConfigureAwait(false);

        return DeduplicateById(allChunks);
    }

    private static List<CodeChunk> DeduplicateById(IEnumerable<CodeChunk> chunks)
    {
        var seen = new HashSet<string>();
        var result = new List<CodeChunk>();
        foreach (var chunk in chunks)
        {
            if (seen.Add(chunk.Id))
                result.Add(chunk);
        }
        return result;
    }

    private IReadOnlyList<CodeChunk> ApplyChunkSplitting(IReadOnlyList<CodeChunk> chunks)
    {
        var result = new List<CodeChunk>();

        foreach (var chunk in chunks)
        {
            if (chunk.EmbeddingText.Length <= _chunkSplitter.MaxChunkChars)
            {
                result.Add(chunk);
                continue;
            }

            var parts = _chunkSplitter.SplitIfNeeded(chunk.EmbeddingText);

            for (var i = 0; i < parts.Count; i++)
            {
                result.Add(chunk with
                {
                    Id = $"{chunk.Id}_part{i}",
                    EmbeddingText = parts[i],
                    PartIndex = i,
                    TotalParts = parts.Count
                });
            }
        }

        return result;
    }

    private static bool IsGeneratedFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Microsoft.NET.Test.Sdk.", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || filePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureMsBuildRegistered()
    {
        if (_msbuildRegistered) return;

        lock (_registrationLock)
        {
            if (_msbuildRegistered) return;
            MSBuildLocator.RegisterDefaults();
            _msbuildRegistered = true;
        }
    }
}
