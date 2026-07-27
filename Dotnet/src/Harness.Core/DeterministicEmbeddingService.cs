using Harness.Abstractions;

namespace Harness.Core;

public class DeterministicEmbeddingService : IEmbeddingService
{
    private const int VectorSize = 32;

    public Task<float[][]> GetEmbeddingsAsync(
        IReadOnlyCollection<string> texts,
        CancellationToken cancellationToken = default)
    {
        var embeddings = texts.Select(CreateEmbedding).ToArray();
        return Task.FromResult(embeddings);
    }

    private static float[] CreateEmbedding(string text)
    {
        var vector = new float[VectorSize];
        foreach (var token in Tokenize(text))
        {
            var hash = StableHash(token);
            var bucket = (int)(hash % VectorSize);
            var sign = (hash & 1) == 0 ? 1f : -1f;
            vector[bucket] += sign;
        }

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        if (norm <= 0f)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new List<char>();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                current.Add(char.ToLowerInvariant(ch));
                continue;
            }

            if (current.Count == 0)
            {
                continue;
            }

            yield return new string(current.ToArray());
            current.Clear();
        }

        if (current.Count > 0)
        {
            yield return new string(current.ToArray());
        }
    }

    private static uint StableHash(string text)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var hash = fnvOffset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }

        return hash;
    }
}
