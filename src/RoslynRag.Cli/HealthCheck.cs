using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace RoslynRag.Cli;

internal static class HealthCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<bool> ValidateAsync(
        string qdrantHost = "localhost",
        int qdrantRestPort = 6333,
        string ollamaBaseUrl = "http://localhost:11434",
        string[]? requiredModels = null,
        CancellationToken ct = default)
    {
        requiredModels ??= ["nomic-embed-text", "llama3:8b"];

        if (!await CheckQdrantAsync(qdrantHost, qdrantRestPort, ct))
            return false;

        if (!await CheckOllamaAsync(ollamaBaseUrl, requiredModels, ct))
            return false;

        return true;
    }

    private static async Task<bool> CheckQdrantAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            var response = await Http.GetAsync($"http://{host}:{port}/healthz", ct);
            if (response.IsSuccessStatusCode)
                return true;
        }
        catch
        {
            // Connection refused or timeout
        }

        AnsiConsole.MarkupLine("[red]Qdrant is not running.[/]");
        AnsiConsole.MarkupLine("  Start it with: [blue]docker compose up -d[/]");
        return false;
    }

    private static async Task<bool> CheckOllamaAsync(string baseUrl, string[] requiredModels, CancellationToken ct)
    {
        OllamaTagsResponse? tags;

        try
        {
            tags = await Http.GetFromJsonAsync(
                $"{baseUrl}/api/tags",
                OllamaHealthJsonContext.Default.OllamaTagsResponse,
                ct);
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]Ollama is not running.[/]");
            AnsiConsole.MarkupLine("  Start it with: [blue]ollama serve[/]");
            return false;
        }

        var installedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tags?.Models is not null)
        {
            foreach (var m in tags.Models)
            {
                if (m.Name is not null)
                {
                    installedModels.Add(m.Name);
                    // Also add without :latest suffix
                    var colonIndex = m.Name.IndexOf(':');
                    if (colonIndex > 0)
                        installedModels.Add(m.Name[..colonIndex]);
                }
            }
        }

        var missing = requiredModels.Where(r => !installedModels.Contains(r)).ToList();
        if (missing.Count == 0)
            return true;

        AnsiConsole.MarkupLine("[red]Missing Ollama models:[/]");
        foreach (var model in missing)
            AnsiConsole.MarkupLine($"  [blue]ollama pull {model}[/]");

        return false;
    }
}

[JsonSerializable(typeof(OllamaTagsResponse))]
internal partial class OllamaHealthJsonContext : JsonSerializerContext;

internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo>? Models { get; init; }
}

internal sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
