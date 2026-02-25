using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IIndexStateStore
{
    Task<SolutionIndexState?> LoadAsync(string solutionPath, CancellationToken ct = default);
    Task<IndexState> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(SolutionIndexState state, CancellationToken ct = default);
    Task DeleteAsync(string solutionPath, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}
