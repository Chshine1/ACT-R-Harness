namespace Harness.Abstractions;

public interface IEmbeddingService
{
    Task<float[][]> GetEmbeddingsAsync(IReadOnlyCollection<string> texts,
        CancellationToken cancellationToken = default);
}