using System.Diagnostics;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Core.Configuration;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Harness.Core;

public class LlmClient : IProvideLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ChatClient _chatClient;
    private readonly ILogger<LlmClient> _logger;

    public ILogger Logger => _logger;

    public LlmClient(IOptions<LlmClientOptions> options, ILogger<LlmClient> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.Value.Model)
            || string.IsNullOrWhiteSpace(options.Value.ApiKey)
            || string.IsNullOrWhiteSpace(options.Value.BaseUrl))
        {
            throw new InvalidOperationException(
                "NeuroCore requires NEURO_LLM_MODEL, OPENAI_API_KEY, and OPENAI_BASE_URL.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/")
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(options.Value.ApiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(options.Value.Model);
    }

    [TraceSpan]
    public async Task<JsonNode?> ChatJsonAsync(
        object? userData,
        string system,
        CancellationToken cancellationToken = default)
    {
        Activity.Current?.SetTag(TracingModel.Tags.LlmPayloadType, userData?.GetType().Name ?? "null");
        TracingModel.AddEvent(
            TracingModel.Events.LlmRequestSubmitted,
            new[]
            {
                new KeyValuePair<string, object?>(
                    TracingModel.Tags.LlmPayloadType,
                    userData?.GetType().Name ?? "null")
            });
        LoggingModel.Log(
            _logger,
            LogLevel.Debug,
            LoggingModel.Events.LlmRequestSubmitted,
            new[]
            {
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.PayloadType,
                    userData?.GetType().Name ?? "null")
            });

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage(JsonSerializer.Serialize(userData, SerializerOptions))
        };

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var completion = response.Value;

        var content = completion.Content;
        if (content == null)
        {
            LoggingModel.Log(
                _logger,
                LogLevel.Warning,
                LoggingModel.Events.LlmResponseReceived,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.ResponseLength, 0)
                });
            return null;
        }

        var stringBuilder = new StringBuilder();
        foreach (var contentPart in content)
        {
            if (contentPart.Kind == ChatMessageContentPartKind.Text)
            {
                stringBuilder.Append(contentPart.Text);
            }
        }

        var fullContent = stringBuilder.ToString();
        TracingModel.AddEvent(
            TracingModel.Events.LlmResponseReceived,
            new[]
            {
                new KeyValuePair<string, object?>(
                    TracingModel.Tags.LlmResponseLength,
                    fullContent.Length)
            });
        LoggingModel.Log(
            _logger,
            LogLevel.Debug,
            LoggingModel.Events.LlmResponseReceived,
            new[]
            {
                new KeyValuePair<string, object?>(
                    LoggingModel.Fields.ResponseLength,
                    fullContent.Length)
            });

        if (string.IsNullOrWhiteSpace(fullContent))
        {
            LoggingModel.Log(
                _logger,
                LogLevel.Warning,
                LoggingModel.Events.LlmResponseReceived,
                new[]
                {
                    new KeyValuePair<string, object?>(LoggingModel.Fields.ResponseLength, 0)
                });
            return null;
        }

        try
        {
            return JsonNode.Parse(fullContent);
        }
        catch (JsonException)
        {
            TracingModel.AddEvent(
                TracingModel.Events.LlmResponseInvalidJson,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        TracingModel.Tags.LlmResponsePreview,
                        fullContent.Length > 200 ? fullContent[..200] : fullContent)
                });
            LoggingModel.Log(
                _logger,
                LogLevel.Warning,
                LoggingModel.Events.LlmResponseInvalidJson,
                new[]
                {
                    new KeyValuePair<string, object?>(
                        LoggingModel.Fields.ResponsePreview,
                        fullContent.Length > 200 ? fullContent[..200] : fullContent)
                });

            return JsonValue.Create(fullContent);
        }
    }
}
