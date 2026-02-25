using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IKeywordIndex
{
    void Initialize();
    void IndexChunks(IReadOnlyList<CodeChunk> chunks);
    IReadOnlyList<SearchResult> Search(string queryText, int topK = 20, string? solutionId = null);
    void DeleteByFilePaths(string solutionId, IReadOnlySet<string> filePaths);
    void DeleteBySolutionId(string solutionId);
    void DeleteAll();
}
