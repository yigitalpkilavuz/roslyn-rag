using RoslynRag.Core.Interfaces;
using RoslynRag.Core.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace RoslynRag.Storage;

public sealed class LuceneKeywordIndex : IKeywordIndex, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    private readonly string _indexPath;
    private readonly System.Threading.Lock _lock = new();
    private StandardAnalyzer? _analyzer;
    private FSDirectory? _directory;
    private IndexWriter? _writer;

    public LuceneKeywordIndex(string indexPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        _indexPath = indexPath;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_writer is not null) return;

            System.IO.Directory.CreateDirectory(_indexPath);
            _analyzer = new StandardAnalyzer(AppLuceneVersion);
            _directory = FSDirectory.Open(_indexPath);
            var config = new IndexWriterConfig(AppLuceneVersion, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };
            _writer = new IndexWriter(_directory, config);
        }
    }

    public void IndexChunks(IReadOnlyList<CodeChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var writer = GetWriter();

        lock (_lock)
        {
            foreach (var chunk in chunks)
            {
                var doc = new Document
                {
                    new StringField("id", chunk.Id, Field.Store.YES),
                    new StringField("solution_id", chunk.SolutionId, Field.Store.YES),
                    new StringField("file_path", chunk.FilePath, Field.Store.YES),
                    new TextField("embedding_text", chunk.EmbeddingText, Field.Store.NO),
                    new StringField("class_name", chunk.ClassName, Field.Store.YES),
                    new StringField("method_name", chunk.MethodName, Field.Store.YES),
                    new Int32Field("start_line", chunk.StartLine, Field.Store.YES),
                    new Int32Field("end_line", chunk.EndLine, Field.Store.YES),
                    new TextField("body", chunk.Body, Field.Store.YES)
                };

                writer.UpdateDocument(new Term("id", chunk.Id), doc);
            }

            writer.Commit();
        }
    }

    public IReadOnlyList<SearchResult> Search(string queryText, int topK = 20, string? solutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(topK, 0);
        var writer = GetWriter();

        lock (_lock)
        {
            using var reader = writer.GetReader(applyAllDeletes: true);
            var searcher = new IndexSearcher(reader);
            var analyzer = _analyzer!;

            var parser = new MultiFieldQueryParser(
                AppLuceneVersion,
                ["embedding_text", "class_name", "method_name"],
                analyzer);

            Query query;
            try
            {
                query = parser.Parse(QueryParserBase.Escape(queryText));
            }
            catch (ParseException)
            {
                return [];
            }

            if (solutionId is not null)
            {
                var filtered = new BooleanQuery
                {
                    { query, Occur.MUST },
                    { new TermQuery(new Term("solution_id", solutionId)), Occur.MUST }
                };
                query = filtered;
            }

            var hits = searcher.Search(query, topK);
            var results = new List<SearchResult>(hits.ScoreDocs.Length);

            foreach (var hit in hits.ScoreDocs)
            {
                var doc = searcher.Doc(hit.Doc);
                var chunkId = doc.Get("id");
                if (chunkId is null) continue;

                results.Add(new SearchResult
                {
                    ChunkId = chunkId,
                    Score = hit.Score,
                    SolutionId = doc.Get("solution_id") ?? string.Empty,
                    FilePath = doc.Get("file_path") ?? string.Empty,
                    ClassName = doc.Get("class_name") ?? string.Empty,
                    MethodName = doc.Get("method_name") ?? string.Empty,
                    StartLine = int.TryParse(doc.Get("start_line"), out var sl) ? sl : 0,
                    EndLine = int.TryParse(doc.Get("end_line"), out var el) ? el : 0,
                    Body = doc.Get("body")
                });
            }

            return results;
        }
    }

    public void DeleteByFilePaths(string solutionId, IReadOnlySet<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var writer = GetWriter();

        lock (_lock)
        {
            foreach (var filePath in filePaths)
            {
                var query = new BooleanQuery
                {
                    { new TermQuery(new Term("solution_id", solutionId)), Occur.MUST },
                    { new TermQuery(new Term("file_path", filePath)), Occur.MUST }
                };
                writer.DeleteDocuments(query);
            }

            writer.Commit();
        }
    }

    public void DeleteBySolutionId(string solutionId)
    {
        var writer = GetWriter();

        lock (_lock)
        {
            writer.DeleteDocuments(new Term("solution_id", solutionId));
            writer.Commit();
        }
    }

    public void DeleteAll()
    {
        var writer = GetWriter();

        lock (_lock)
        {
            writer.DeleteAll();
            writer.Commit();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _directory?.Dispose();
            _analyzer?.Dispose();
            _writer = null;
            _directory = null;
            _analyzer = null;
        }
    }

    private IndexWriter GetWriter()
        => _writer ?? throw new InvalidOperationException("Index not initialized. Call Initialize() first.");
}
