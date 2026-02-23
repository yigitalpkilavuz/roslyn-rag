using RoslynRag.Core.Models;

namespace RoslynRag.Core.Interfaces;

public interface IIndexStateStore
{
    Task<IndexState?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IndexState state, CancellationToken ct = default);
    Task DeleteAsync(CancellationToken ct = default);
}
