using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Indexing;

public sealed class IndexingPipeline
{
    private readonly IParsePipeline _parser;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordIndex _keywordIndex;
    private readonly IIndexStateStore _stateStore;
    private readonly IGitDiffDetector _gitDiff;

    public IndexingPipeline(
        IParsePipeline parser,
        IEmbeddingService embedding,
        IVectorStore vectorStore,
        IKeywordIndex keywordIndex,
        IIndexStateStore stateStore,
        IGitDiffDetector gitDiff)
    {
        _parser = parser;
        _embedding = embedding;
        _vectorStore = vectorStore;
        _keywordIndex = keywordIndex;
        _stateStore = stateStore;
        _gitDiff = gitDiff;
    }

    public async Task IndexAsync(
        string solutionPath,
        bool forceFullIndex = false,
        IProgress<string>? statusProgress = null,
        IProgress<(int completed, int total)>? embeddingProgress = null,
        CancellationToken ct = default)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var state = await _stateStore.LoadAsync(ct).ConfigureAwait(false);
        string? currentCommitSha = null;

        try
        {
            currentCommitSha = await _gitDiff.GetCurrentCommitShaAsync(solutionDir, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            statusProgress?.Report("Warning: Not a git repository or git not available. Performing full index.");
        }

        var isIncremental = !forceFullIndex
            && state?.LastIndexedCommitSha is not null
            && currentCommitSha is not null;

        if (isIncremental)
        {
            await IncrementalIndexAsync(solutionPath, solutionDir, state!, currentCommitSha!,
                statusProgress, embeddingProgress, ct).ConfigureAwait(false);
        }
        else
        {
            await FullIndexAsync(solutionPath, currentCommitSha,
                statusProgress, embeddingProgress, ct).ConfigureAwait(false);
        }
    }

    private async Task FullIndexAsync(
        string solutionPath,
        string? commitSha,
        IProgress<string>? statusProgress,
        IProgress<(int completed, int total)>? embeddingProgress,
        CancellationToken ct)
    {
        statusProgress?.Report("Starting full index...");

        await _vectorStore.InitializeAsync(_embedding.Dimensions, ct).ConfigureAwait(false);
        _keywordIndex.Initialize();

        statusProgress?.Report("Parsing solution with Roslyn...");
        var parseProgress = new Progress<(int processed, int total, string currentFile)>(p =>
            statusProgress?.Report($"Parsing [{p.processed}/{p.total}]: {Path.GetFileName(p.currentFile)}"));

        var chunks = await _parser.ParseSolutionAsync(solutionPath, parseProgress, ct).ConfigureAwait(false);
        statusProgress?.Report($"Parsed {chunks.Count} chunks.");

        if (chunks.Count == 0)
        {
            statusProgress?.Report("No chunks found. Index is empty.");
            return;
        }

        statusProgress?.Report("Generating embeddings...");
        var embeddings = await _embedding.EmbedBatchAsync(chunks, embeddingProgress, ct).ConfigureAwait(false);

        statusProgress?.Report("Storing in Qdrant...");
        await _vectorStore.UpsertAsync(chunks, embeddings, ct).ConfigureAwait(false);

        statusProgress?.Report("Building BM25 index...");
        _keywordIndex.IndexChunks(chunks);

        var distinctFiles = chunks.Select(c => c.FilePath).Distinct().Count();
        await _stateStore.SaveAsync(new IndexState
        {
            SolutionPath = Path.GetFullPath(solutionPath),
            LastIndexedCommitSha = commitSha,
            IndexedAt = DateTimeOffset.UtcNow,
            TotalChunks = chunks.Count,
            TotalFiles = distinctFiles,
            EmbeddingModel = _embedding.ModelName,
            EmbeddingDimensions = _embedding.Dimensions
        }, ct).ConfigureAwait(false);

        statusProgress?.Report($"Full index complete: {chunks.Count} chunks from {distinctFiles} files.");
    }

    private async Task IncrementalIndexAsync(
        string solutionPath,
        string solutionDir,
        IndexState state,
        string currentCommitSha,
        IProgress<string>? statusProgress,
        IProgress<(int completed, int total)>? embeddingProgress,
        CancellationToken ct)
    {
        statusProgress?.Report($"Incremental index: {state.LastIndexedCommitSha![..8]} → {currentCommitSha[..8]}");

        var diff = await _gitDiff.GetChangedFilesAsync(
            solutionDir, state.LastIndexedCommitSha!, currentCommitSha, ct).ConfigureAwait(false);

        var allChanged = diff.AllChangedFiles;
        if (allChanged.Count == 0)
        {
            statusProgress?.Report("No .cs files changed. Index is up to date.");
            return;
        }

        statusProgress?.Report($"Changed files: {allChanged.Count} ({diff.AddedFiles.Count} added, {diff.ModifiedFiles.Count} modified, {diff.DeletedFiles.Count} deleted)");

        _keywordIndex.Initialize();

        var filesToDelete = new HashSet<string>(diff.ModifiedFiles.Concat(diff.DeletedFiles), StringComparer.OrdinalIgnoreCase);
        if (filesToDelete.Count > 0)
        {
            statusProgress?.Report($"Removing stale chunks for {filesToDelete.Count} files...");
            await _vectorStore.DeleteByFilePathsAsync(filesToDelete, ct).ConfigureAwait(false);
            _keywordIndex.DeleteByFilePaths(filesToDelete);
        }

        var filesToParse = new HashSet<string>(diff.AddedFiles.Concat(diff.ModifiedFiles), StringComparer.OrdinalIgnoreCase);
        if (filesToParse.Count > 0)
        {
            statusProgress?.Report($"Parsing {filesToParse.Count} changed files...");
            var chunks = await _parser.ParseFilesAsync(solutionPath, filesToParse, ct).ConfigureAwait(false);

            if (chunks.Count > 0)
            {
                statusProgress?.Report($"Generating embeddings for {chunks.Count} chunks...");
                var embeddings = await _embedding.EmbedBatchAsync(chunks, embeddingProgress, ct).ConfigureAwait(false);

                statusProgress?.Report("Updating stores...");
                await _vectorStore.UpsertAsync(chunks, embeddings, ct).ConfigureAwait(false);
                _keywordIndex.IndexChunks(chunks);
            }
        }

        var totalChunks = await _vectorStore.GetPointCountAsync(ct).ConfigureAwait(false);
        state.LastIndexedCommitSha = currentCommitSha;
        state.IndexedAt = DateTimeOffset.UtcNow;
        state.TotalChunks = (int)totalChunks;
        await _stateStore.SaveAsync(state, ct).ConfigureAwait(false);

        statusProgress?.Report($"Incremental index complete. Total chunks: {totalChunks}");
    }
}
