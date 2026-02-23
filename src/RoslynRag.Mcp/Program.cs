using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;
using RoslynRag.Indexing;
using RoslynRag.Parsing;
using RoslynRag.Storage;

var config = LoadConfig();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<HttpClient>(_ => new HttpClient
{
    BaseAddress = new Uri(config.Ollama.BaseUrl),
    Timeout = TimeSpan.FromMinutes(5)
});

builder.Services.AddSingleton<IEmbeddingService>(sp =>
    new OllamaEmbeddingService(
        sp.GetRequiredService<HttpClient>(),
        config.Ollama.EmbeddingModel,
        config.Ollama.EmbeddingDimensions,
        config.Ollama.BatchSize));

builder.Services.AddSingleton<IVectorStore>(_ =>
    new QdrantVectorStore(config.Qdrant.Host, config.Qdrant.GrpcPort));

builder.Services.AddSingleton<IKeywordIndex>(_ =>
    new LuceneKeywordIndex($"{config.Indexing.DataDirectory}/lucene-index"));

builder.Services.AddSingleton<ISearchFusion>(_ =>
    new RrfSearchFusion(config.Search.RrfK));

builder.Services.AddSingleton<IIndexStateStore>(_ =>
    new JsonIndexStateStore(config.Indexing.DataDirectory));

builder.Services.AddSingleton<IChunkSplitter>(_ =>
    new TreeSitterChunkSplitter(config.Indexing.MaxChunkChars));

builder.Services.AddSingleton<IParsePipeline>(sp =>
    new RoslynParsePipeline(sp.GetRequiredService<IChunkSplitter>()));

builder.Services.AddSingleton<IGitDiffDetector>(_ =>
    new GitDiffDetector());

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "roslyn-rag",
            Version = "0.1.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

static RoslynRagConfig LoadConfig()
{
    const string fileName = "roslyn-rag.json";
    var path = Path.Combine(Directory.GetCurrentDirectory(), fileName);

    if (!File.Exists(path))
        return new RoslynRagConfig();

    try
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, McpConfigJsonContext.Default.RoslynRagConfig)
            ?? new RoslynRagConfig();
    }
    catch (JsonException)
    {
        return new RoslynRagConfig();
    }
}

[JsonSerializable(typeof(RoslynRagConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class McpConfigJsonContext : JsonSerializerContext;
