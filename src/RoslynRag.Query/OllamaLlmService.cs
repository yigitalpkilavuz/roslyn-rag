using System.Net.Http.Json;
using System.Text.Json.Serialization;
using RoslynRag.Core.Interfaces;

namespace RoslynRag.Query;

public sealed class OllamaLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaLlmService(HttpClient httpClient, string model = "llama3:8b")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false
        };

        var response = await _httpClient.PostAsJsonAsync("/api/generate", request, LlmJsonContext.Default.OllamaGenerateRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(LlmJsonContext.Default.OllamaGenerateResponse, ct).ConfigureAwait(false);
        return result?.Response ?? string.Empty;
    }
}

[JsonSerializable(typeof(OllamaGenerateRequest))]
[JsonSerializable(typeof(OllamaGenerateResponse))]
internal partial class LlmJsonContext : JsonSerializerContext;

internal sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

internal sealed class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }
}
