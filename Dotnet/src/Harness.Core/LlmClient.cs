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

    [ObserveBoundary]
    public async Task<JsonNode?> ChatJsonAsync(
        object? userData,
        string system,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage(JsonSerializer.Serialize(userData, SerializerOptions))
        };

        ChatCompletion completion;
        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            completion = response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM request failed.");
            throw;
        }

        var content = completion.Content;
        if (content == null)
        {
            _logger.LogWarning("LLM response content was empty.");
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

        if (string.IsNullOrWhiteSpace(fullContent))
        {
            _logger.LogWarning("LLM response content was empty.");
            return null;
        }

        try
        {
            return JsonNode.Parse(fullContent);
        }
        catch (JsonException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("LLM response was not valid JSON. Preview: {Preview}",
                    fullContent.Length > 200 ? fullContent[..200] : fullContent);
            }

            return JsonValue.Create(fullContent);
        }
    }
}