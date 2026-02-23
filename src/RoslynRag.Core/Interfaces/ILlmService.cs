namespace RoslynRag.Core.Interfaces;

public interface ILlmService
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
}
