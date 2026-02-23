using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface ISearchFusion
{
    IReadOnlyList<FusedSearchResult> Fuse(
        IReadOnlyList<SearchResult> vectorResults,
        IReadOnlyList<SearchResult> bm25Results,
        int topK = 10);
}
