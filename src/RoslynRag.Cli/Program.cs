using System.CommandLine;
using RoslynRag.Cli.Commands;
using RoslynRag.Core.Interfaces;
using RoslynRag.Indexing;
using RoslynRag.Parsing;
using RoslynRag.Query;
using RoslynRag.Storage;

const string DataDirectory = ".roslyn-rag";
const string LuceneIndexPath = $"{DataDirectory}/lucene-index";
const string QdrantHost = "localhost";
const int QdrantPort = 6334;
const string OllamaBaseUrl = "http://localhost:11434";
const string EmbeddingModel = "nomic-embed-text";
const string LlmModel = "llama3:8b";
const int EmbeddingDimensions = 768;
const int BatchSize = 32;

var ollamaHttpClient = new HttpClient
{
    BaseAddress = new Uri(OllamaBaseUrl),
    Timeout = TimeSpan.FromMinutes(5)
};

IChunkSplitter ChunkSplitter() => new TreeSitterChunkSplitter();
IParsePipeline Parser() => new RoslynParsePipeline(ChunkSplitter());
IEmbeddingService Embedding() => new OllamaEmbeddingService(ollamaHttpClient, EmbeddingModel, EmbeddingDimensions, BatchSize);
IVectorStore VectorStore() => new QdrantVectorStore(QdrantHost, QdrantPort);
IKeywordIndex KeywordIndex() => new LuceneKeywordIndex(LuceneIndexPath);
ISearchFusion Fusion() => new RrfSearchFusion();
ILlmService Llm() => new OllamaLlmService(ollamaHttpClient, LlmModel);
IIndexStateStore StateStore() => new JsonIndexStateStore(DataDirectory);
IGitDiffDetector GitDiff() => new GitDiffDetector();

IndexingPipeline IndexingPipeline() => new(
    Parser(), Embedding(), VectorStore(), KeywordIndex(), StateStore(), GitDiff());

QueryPipeline QueryPipelineFactory() => new(
    Embedding(), VectorStore(), KeywordIndex(), Fusion(), Llm());

var rootCommand = new RootCommand("Roslyn RAG — Local RAG-based code intelligence assistant for .NET")
{
    IndexCommand.Create(() => IndexingPipeline()),
    QueryCommand.Create(() => QueryPipelineFactory()),
    StatusCommand.Create(() => StateStore(), () => VectorStore()),
    ResetCommand.Create(() => VectorStore(), () => KeywordIndex(), () => StateStore())
};

return await rootCommand.Parse(args).InvokeAsync();
