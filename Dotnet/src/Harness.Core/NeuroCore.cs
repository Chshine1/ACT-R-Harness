using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;
using Microsoft.Extensions.Logging;

namespace Harness.Core;

public class NeuroCore(
    Abstractions.Actr.Services.NeuroCore.NeuroCoreClient client,
    ILogger<NeuroCore> logger)
    : INeuroCore
{
    public async Task<IReadOnlyList<string>> EvaluateConditionsAsync(
        IReadOnlyList<ProceduralCondition> conditions,
        IReadOnlyList<BufferState> bufferStates,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation(
            "NeuroCore.EvaluateConditions request: rules={RuleCount}, buffers={BufferCount}",
            conditions.Count,
            bufferStates.Count);

        try
        {
            var response = await client.EvaluateConditionsAsync(
                new EvaluateConditionsRequest
                {
                    Conditions = { conditions },
                    BufferStates = { bufferStates }
                },
                cancellationToken: cancellationToken
            );

            logger.LogInformation(
                "NeuroCore.EvaluateConditions response: satisfied={SatisfiedRuleCount}, ruleIds={SatisfiedRuleIds}",
                response.SatisfiedRuleIds.Count,
                response.SatisfiedRuleIds.ToArray());
            return response.SatisfiedRuleIds;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "NeuroCore.EvaluateConditions failed for rules={RuleCount}, buffers={BufferCount}",
                conditions.Count,
                bufferStates.Count);
            throw;
        }
    }

    public async Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
        NeuroAction actionIntent,
        IReadOnlyList<BufferState> currentStates,
        IReadOnlyList<ModuleSchema> schemas,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation(
            "NeuroCore.DecodeAction request: rule={RuleId}, buffers={BufferCount}, schemas={SchemaCount}, commands={CommandCount}, semantics={SemanticCount}",
            actionIntent.RuleId,
            currentStates.Count,
            schemas.Count,
            actionIntent.Commands.Count,
            actionIntent.Semantics.Count);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "NeuroCore.DecodeAction payload: commandAliases={CommandAliases}, semanticKeys={SemanticKeys}",
                actionIntent.Commands.Keys.ToArray(),
                actionIntent.Semantics.Keys.ToArray());
        }

        try
        {
            var response = await client.DecodeActionAsync(
                new DecodeActionRequest
                {
                    ActionIntent = actionIntent,
                    CurrentStates = { currentStates },
                    Schemas = { schemas }
                },
                cancellationToken: cancellationToken
            );

            logger.LogInformation(
                "NeuroCore.DecodeAction response: operations={OperationCount}",
                response.Operations.Count);
            return response.Operations;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "NeuroCore.DecodeAction failed for rule={RuleId}",
                actionIntent.RuleId);
            throw;
        }
    }
}
