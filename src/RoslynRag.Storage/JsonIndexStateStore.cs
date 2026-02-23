using System.Text.Json;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;

namespace RoslynRag.Storage;

public sealed class JsonIndexStateStore : IIndexStateStore
{
    private readonly string _stateFilePath;

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

    public async Task<IndexState?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_stateFilePath))
            return null;

        try
        {
            await using var stream = File.OpenRead(_stateFilePath);
            return await JsonSerializer.DeserializeAsync<IndexState>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Corrupt state file — treat as if no state exists
            return null;
        }
    }

    public async Task SaveAsync(IndexState state, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(CancellationToken ct = default)
    {
        if (File.Exists(_stateFilePath))
            File.Delete(_stateFilePath);

        return Task.CompletedTask;
    }
}
