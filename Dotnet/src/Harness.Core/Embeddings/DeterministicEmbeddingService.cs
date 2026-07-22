using Harness.Abstractions;

namespace Harness.Core.Embeddings;

public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    private const int Dimensions = 16;

    public Task<float[][]> GetEmbeddingsAsync(
        IReadOnlyCollection<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var embeddings = new float[texts.Count][];
        var index = 0;

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings[index++] = CreateEmbedding(text);
        }

        return Task.FromResult(embeddings);
    }

    private static float[] CreateEmbedding(string? text)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            vector[0] = 1f;
            return vector;
        }

        foreach (var character in text.Trim().ToLowerInvariant())
        {
            vector[character % Dimensions] += 1f;
        }

        var sumOfSquares = 0d;
        foreach (var value in vector)
        {
            sumOfSquares += value * value;
        }

        if (sumOfSquares == 0d)
        {
            vector[0] = 1f;
            return vector;
        }

        var scale = (float)(1d / Math.Sqrt(sumOfSquares));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= scale;
        }

        return vector;
    }
}
