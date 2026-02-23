namespace RoslynRag.Core.Models;

public sealed class GitDiffResult
{
    public required IReadOnlySet<string> AddedFiles { get; init; }
    public required IReadOnlySet<string> ModifiedFiles { get; init; }
    public required IReadOnlySet<string> DeletedFiles { get; init; }

    private IReadOnlySet<string>? _allChangedFiles;
    public IReadOnlySet<string> AllChangedFiles =>
        _allChangedFiles ??= new HashSet<string>(AddedFiles.Concat(ModifiedFiles).Concat(DeletedFiles));
}
