namespace RoslynRag.Core.Interfaces;

public interface IChunkSplitter
{
    int MaxChunkChars { get; }
    IReadOnlyList<string> SplitIfNeeded(string sourceText);
}
