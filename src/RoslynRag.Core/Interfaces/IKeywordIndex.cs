using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IKeywordIndex
{
    void Initialize();
    void IndexChunks(IReadOnlyList<CodeChunk> chunks);
    IReadOnlyList<SearchResult> Search(string queryText, int topK = 20);
    void DeleteByFilePaths(IReadOnlySet<string> filePaths);
    void DeleteAll();
}
