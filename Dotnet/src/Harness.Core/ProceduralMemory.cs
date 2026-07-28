using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class ProceduralMemory : IProceduralMemory
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

    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        _logger.LogInformation("ProceduralMemory.GetAllConditions request");

        try
        {
            var response = _client.GetAllConditions(new Empty());
            _logger.LogInformation(
                "ProceduralMemory.GetAllConditions response: rules={RuleCount}",
                response.Conditions.Count);
            return response.Conditions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProceduralMemory.GetAllConditions failed.");
            throw;
        }
    }

    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        _logger.LogInformation(
            "ProceduralMemory.SelectRule request: candidates={CandidateCount}, ruleIds={CandidateRuleIds}",
            satisfiedRuleIds.Count,
            satisfiedRuleIds.ToArray());

        try
        {
            var response = _client.SelectRule(
                new SelectRuleRequest
                {
                    SatisfiedRuleIds = { satisfiedRuleIds }
                }
            );

            _lastRuleId = response.RuleId;
            _logger.LogInformation(
                "ProceduralMemory.SelectRule response: selected={RuleId}",
                response.RuleId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ProceduralMemory.SelectRule failed for candidates={CandidateCount}",
                satisfiedRuleIds.Count);
            throw;
        }
    }

    private async Task LearnUtilityAsync(float reward, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "ProceduralMemory.LearnUtility request: rule={RuleId}, reward={Reward}",
            _lastRuleId,
            reward);

        await _client.LearnUtilityAsync(
            new LearnUtilityRequest
            {
                RuleId = _lastRuleId,
                Reward = reward
            }, cancellationToken: cancellationToken
        );
    }
}
