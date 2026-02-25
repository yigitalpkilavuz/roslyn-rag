using System.Text.Json;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Storage;

public sealed class JsonIndexStateStore : IIndexStateStore
{
    private readonly string _stateFilePath;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonIndexStateStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _stateFilePath = Path.Combine(dataDirectory, "index-state.json");
    }

    public async Task<SolutionIndexState?> LoadAsync(string solutionPath, CancellationToken ct = default)
    {
        var state = await LoadAllAsync(ct).ConfigureAwait(false);
        return state.Solutions.GetValueOrDefault(solutionPath);
    }

    public async Task<IndexState> LoadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_stateFilePath))
            return new IndexState();

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<IndexState>(json, JsonOptions) ?? new IndexState();
        }
        catch (JsonException)
        {
            return new IndexState();
        }
    }

    public Task SaveAsync(SolutionIndexState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var root = LoadAllSync();
            root.Solutions[state.SolutionPath] = state;
            WriteStateSync(root);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string solutionPath, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var root = LoadAllSync();
            root.Solutions.Remove(solutionPath);

            if (root.Solutions.Count == 0 && File.Exists(_stateFilePath))
                File.Delete(_stateFilePath);
            else
                WriteStateSync(root);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        if (File.Exists(_stateFilePath))
            File.Delete(_stateFilePath);

        return Task.CompletedTask;
    }

    private IndexState LoadAllSync()
    {
        if (!File.Exists(_stateFilePath))
            return new IndexState();

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<IndexState>(json, JsonOptions) ?? new IndexState();
        }
        catch (JsonException)
        {
            return new IndexState();
        }
    }

    private void WriteStateSync(IndexState state)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_stateFilePath, json);
    }
}
