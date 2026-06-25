using System.Numerics;

namespace Harness.Shared.Utils;

public static class NumericUtils
{
    public static float[] AverageVectors(List<float[]> vectors)
    {
        if (vectors.Count == 0) throw new ArgumentException("Vectors cannot be empty");

        var dim = vectors[0].Length;
        var result = new float[dim];
        var vectorSize = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && dim >= vectorSize)
        {
            foreach (var vec in vectors)
            {
                var i = 0;
                for (; i <= dim - vectorSize; i += vectorSize)
                {
                    var vResult = new Vector<float>(result, i);
                    var vVec = new Vector<float>(vec, i);
                    vResult += vVec;
                    vResult.CopyTo(result, i);
                }

                for (; i < dim; i++)
                {
                    result[i] += vec[i];
                }
            }

            var divisor = new Vector<float>(vectors.Count);
            for (var i = 0; i <= dim - vectorSize; i += vectorSize)
            {
                var v = new Vector<float>(result, i);
                v /= divisor;
                v.CopyTo(result, i);
            }

            for (var i = dim - (dim % vectorSize); i < dim; i++)
            {
                result[i] /= vectors.Count;
            }
        }
        else
        {
            foreach (var vec in vectors)
                for (var i = 0; i < dim; i++)
                    result[i] += vec[i];
            for (var i = 0; i < dim; i++)
                result[i] /= vectors.Count;
        }

        return result;
    }
    
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimensions");

        var length = a.Length;
        float dot = 0f, normA = 0f, normB = 0f;
        var i = 0;

        var vectorSize = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated && length >= vectorSize)
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            for (; i <= length - vectorSize; i += vectorSize)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                vDot += va * vb;
                vNormA += va * va;
                vNormB += vb * vb;
            }

            dot = Vector.Dot(vDot, Vector<float>.One);
            normA = Vector.Dot(vNormA, Vector<float>.One);
            normB = Vector.Dot(vNormB, Vector<float>.One);
        }
        
        for (; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
    }
}