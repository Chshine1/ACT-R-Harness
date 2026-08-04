using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Harness.Core.Configuration;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Core.Modules;

public class DeclarativeMemoryService(
    IOptions<DeclarativeMemoryOptions> options,
    ILogger<DeclarativeMemoryService> logger)
    : IProvideLogger
{
    private readonly Dictionary<string, MemoryChunk> _chunks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<double>> _accessLog = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private readonly Random _random = new();
    private readonly double _decay = options.Value.Decay;
    private readonly double _noiseSd = options.Value.NoiseSd;
    private double _simulationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public ILogger Logger => logger;

    [TraceSpan]
    public void AddChunk(Abstractions.Actr.Services.AddChunkRequest request)
    {
        var chunk = request.Chunk.Clone();

        lock (_sync)
        {
            _chunks[chunk.Id] = chunk;
            _accessLog[chunk.Id] = [_simulationTime];
        }
    }

    [TraceSpan]
    public RetrieveResponse Retrieve(RetrieveRequest request)
    {
        lock (_sync)
        {
            MemoryChunk? bestChunk = null;
            var bestActivation = double.NegativeInfinity;
            var now = _simulationTime;

            foreach (var chunk in _chunks.Values)
            {
                var cueScore = CueScore(chunk, request.Cue);
                if (cueScore <= 0)
                {
                    continue;
                }

                var baseActivation = BaseActivation(chunk.Id, now);
                var noise = _noiseSd > 0 ? NextGaussian() * _noiseSd : 0.0;
                var activation = baseActivation + cueScore + noise;
                if (activation <= bestActivation)
                {
                    continue;
                }

                bestActivation = activation;
                bestChunk = chunk;
            }

            if (bestChunk is not null)
            {
                _accessLog[bestChunk.Id].Add(now);
            }

            return new RetrieveResponse
            {
                Chunk = bestChunk?.Clone()
            };
        }
    }

    [TraceSpan]
    public Task TickMemoryAsync(
        TickMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _simulationTime += request.DeltaTime;
        }

        return Task.CompletedTask;
    }

    private double BaseActivation(string chunkId, double currentTime)
    {
        if (!_accessLog.TryGetValue(chunkId, out var references))
        {
            return -1e6;
        }

        var sumTerm = references.Sum(reference => Math.Pow(currentTime - reference, -_decay));
        if (sumTerm <= 0)
        {
            return -1e6;
        }

        return Math.Log(sumTerm);
    }

    private double CueScore(MemoryChunk chunk, IReadOnlyDictionary<string, string> cue)
    {
        if (cue.Count == 0)
        {
            return 0.0;
        }

        var total = 0.0;
        foreach (var cueEntry in cue)
        {
            chunk.Slots.TryGetValue(cueEntry.Key, out var actual);
            var score = SlotMatchScore(actual, cueEntry.Value);
            if (score <= 0)
            {
                return 0.0;
            }

            total += score;
        }

        return total;
    }

    private static double SlotMatchScore(string? actual, string expected)
    {
        if (actual is null)
        {
            return 0.0;
        }

        var normalizedActual = Normalize(actual);
        var normalizedExpected = Normalize(expected);
        if (string.IsNullOrEmpty(normalizedActual) || string.IsNullOrEmpty(normalizedExpected))
        {
            return 0.0;
        }

        if (normalizedActual == normalizedExpected)
        {
            return 1.0;
        }

        var actualTokens = normalizedActual.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var expectedTokens = normalizedExpected.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var overlap = actualTokens.Intersect(expectedTokens, StringComparer.Ordinal).ToArray();
        if (overlap.Length == 0)
        {
            return 0.0;
        }

        if (actualTokens.IsSubsetOf(expectedTokens) || expectedTokens.IsSubsetOf(actualTokens))
        {
            return 0.85;
        }

        return (double)overlap.Length / Math.Max(actualTokens.Count, expectedTokens.Count);
    }

    private static string Normalize(string text)
    {
        var buffer = text.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ').ToArray();
        return string.Join(' ', new string(buffer).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private double NextGaussian()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
