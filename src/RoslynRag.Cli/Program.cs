using System.CommandLine;
using RoslynRag.Cli;
using RoslynRag.Cli.Commands;
using RoslynRag.Core.Interfaces;
using RoslynRag.Indexing;
using RoslynRag.Parsing;
using RoslynRag.Query;
using RoslynRag.Storage;

var config = ConfigLoader.Load();
var luceneIndexPath = $"{config.Indexing.DataDirectory}/lucene-index";

var ollamaHttpClient = new HttpClient
{
    BaseAddress = new Uri(config.Ollama.BaseUrl),
    Timeout = TimeSpan.FromMinutes(5)
};

IChunkSplitter ChunkSplitter() => new TreeSitterChunkSplitter(config.Indexing.MaxChunkChars);
IParsePipeline Parser() => new RoslynParsePipeline(ChunkSplitter());
IEmbeddingService Embedding() => new OllamaEmbeddingService(ollamaHttpClient, config.Ollama.EmbeddingModel, config.Ollama.EmbeddingDimensions, config.Ollama.BatchSize);
IVectorStore VectorStore() => new QdrantVectorStore(config.Qdrant.Host, config.Qdrant.GrpcPort);
IKeywordIndex KeywordIndex() => new LuceneKeywordIndex(luceneIndexPath);
ISearchFusion Fusion() => new RrfSearchFusion(config.Search.RrfK);
ILlmService Llm() => new OllamaLlmService(ollamaHttpClient, config.Ollama.LlmModel);
IIndexStateStore StateStore() => new JsonIndexStateStore(config.Indexing.DataDirectory);
IGitDiffDetector GitDiff() => new GitDiffDetector();

IndexingPipeline IndexingPipeline() => new(
    Parser(), Embedding(), VectorStore(), KeywordIndex(), StateStore(), GitDiff());

QueryPipeline QueryPipelineFactory() => new(
    Embedding(), VectorStore(), KeywordIndex(), Fusion(), Llm());

var rootCommand = new RootCommand("Roslyn RAG — Local RAG-based code intelligence assistant for .NET")
{
    InitCommand.Create(),
    IndexCommand.Create(() => IndexingPipeline(), config),
    QueryCommand.Create(() => QueryPipelineFactory(), config),
    StatusCommand.Create(() => StateStore(), () => VectorStore()),
    ResetCommand.Create(() => VectorStore(), () => KeywordIndex(), () => StateStore())
};

return await rootCommand.Parse(args).InvokeAsync();
