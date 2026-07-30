using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Harness.Core.Observability;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class ProceduralMemory : IProceduralMemory, IProvideLogger
{
    private readonly Abstractions.Actr.Services.ProceduralMemory.ProceduralMemoryClient _client;
    private readonly ILogger<ProceduralMemory> _logger;
    private string? _lastRuleId;

    public ProceduralMemory(
        Abstractions.Actr.Services.ProceduralMemory.ProceduralMemoryClient client,
        IClock clock,
        ILogger<ProceduralMemory> logger)
    {
        _client = client;
        _logger = logger;
        clock.OnTickAsync += (reward, ct) =>
        {
            if (!reward.Training) return Task.CompletedTask;
            return _lastRuleId == null ? throw new InvalidOperationException() : LearnUtilityAsync(reward.Reward, ct);
        };
    }

    public ILogger Logger => _logger;

    [ObserveBoundary]
    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        using var call = GrpcObservabilityCall.Begin("procedural_memory.get_all_conditions");
        var response = _client.GetAllConditions(new Empty(), headers: call.Headers);
        return response.Conditions;
    }

    [ObserveBoundary]
    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        using var call = GrpcObservabilityCall.Begin("procedural_memory.select_rule");
        var response = _client.SelectRule(
            new SelectRuleRequest
            {
                SatisfiedRuleIds = { satisfiedRuleIds }
            },
            headers: call.Headers
        );

        _lastRuleId = response.RuleId;
        return response;
    }

    [ObserveBoundary]
    private async Task LearnUtilityAsync(float reward, CancellationToken cancellationToken = default)
    {
        using var call = GrpcObservabilityCall.Begin("procedural_memory.learn_utility");
        await _client.LearnUtilityAsync(
            new LearnUtilityRequest
            {
                RuleId = _lastRuleId,
                Reward = reward
            },
            headers: call.Headers,
            cancellationToken: cancellationToken
        );
    }
}
