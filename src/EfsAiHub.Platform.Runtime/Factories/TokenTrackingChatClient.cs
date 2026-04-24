using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Core.Abstractions.AgUi;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// IChatClient delegante que rastreia uso de tokens em cada chamada LLM
/// e enfileira persistência via channel em background.
/// Envolve GetResponseAsync e GetStreamingResponseAsync.
/// </summary>
public class TokenTrackingChatClient : DelegatingChatClient
{
    private readonly string _agentId;
    private readonly string _modelId;
    private readonly ChannelWriter<LlmTokenUsage> _usageWriter;
    private readonly ILogger _logger;
    private readonly IModelPricingCache? _pricingCache;
    private readonly IAgUiTokenSink? _tokenSink;

    public TokenTrackingChatClient(
        IChatClient innerClient,
        string agentId,
        string modelId,
        ChannelWriter<LlmTokenUsage> usageWriter,
        ILogger logger,
        IModelPricingCache? pricingCache = null,
        IAgUiTokenSink? tokenSink = null)
        : base(innerClient)
    {
        _agentId = agentId;
        _modelId = modelId;
        _usageWriter = usageWriter;
        _logger = logger;
        _pricingCache = pricingCache;
        _tokenSink = tokenSink;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnforceBudget();

        using var activity = ActivitySources.LlmCallSource.StartActivity("LLMCall");
        activity?.SetTag("agent.id", _agentId);
        activity?.SetTag("model.id", _modelId);
        activity.SetPersonaTags(DelegateExecutor.Current.Value?.Persona);

        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        sw.Stop();

        // Captura output content truncado a 4000 chars para não explodir o DB.
        var outputContent = response.Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .Select(m => string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text)))
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));
        if (outputContent?.Length > 4000) outputContent = outputContent[..4000];

        TrackUsage(response.Usage, sw.Elapsed.TotalMilliseconds, outputContent, activity);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnforceBudget();

        using var activity = ActivitySources.LlmCallSource.StartActivity("LLMCall.Streaming");
        activity?.SetTag("agent.id", _agentId);
        activity?.SetTag("model.id", _modelId);
        activity.SetPersonaTags(DelegateExecutor.Current.Value?.Persona);

        var sw = Stopwatch.StartNew();
        UsageDetails? lastUsage = null;
        var outputBuilder = new System.Text.StringBuilder();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent uc) lastUsage = uc.Details;
                if (content is TextContent tc) outputBuilder.Append(tc.Text);

                // AG-UI: emite TOOL_CALL_ARGS durante streaming de argumentos de tool calls.
                if (content is FunctionCallContent fcc && _tokenSink is not null)
                {
                    var ctx = DelegateExecutor.Current.Value;
                    if (ctx?.ExecutionId is { } execId)
                    {
                        var argsJson = fcc.Arguments is not null
                            ? JsonSerializer.Serialize(fcc.Arguments)
                            : "{}";
                        _tokenSink.WriteToolCallArgs(execId, fcc.CallId ?? "", fcc.Name ?? "", argsJson);
                    }
                }
            }
            yield return update;
        }

        sw.Stop();
        var streamedOutput = outputBuilder.Length > 0 ? outputBuilder.ToString() : null;
        if (streamedOutput?.Length > 4000) streamedOutput = streamedOutput[..4000];
        TrackUsage(lastUsage, sw.Elapsed.TotalMilliseconds, streamedOutput, activity);
    }

    private static void EnforceBudget()
    {
        var budget = DelegateExecutor.Current.Value?.Budget;
        if (budget is null) return;
        if (budget.IsCostExceeded)
        {
            MetricsRegistry.BudgetExceededKills.Add(1);
            throw new BudgetExceededException(budget.TotalCostUsd, budget.MaxCostUsd ?? 0m, budget.TotalTokens);
        }
        if (budget.IsExceeded)
        {
            MetricsRegistry.BudgetExceededKills.Add(1);
            throw new BudgetExceededException(budget.TotalTokens, budget.MaxTokensPerExecution);
        }
    }

    private void TrackUsage(UsageDetails? usage, double durationMs, string? outputContent = null, Activity? activity = null)
    {
        var inputTokens = (int)(usage?.InputTokenCount ?? 0);
        var outputTokens = (int)(usage?.OutputTokenCount ?? 0);
        var totalTokens = (int)(usage?.TotalTokenCount ?? inputTokens + outputTokens);
        // Propriedade tipada em Microsoft.Extensions.AI.Abstractions 10.5.0;
        // mapeada do OpenAI via InputTokenDetails.CachedTokenCount.
        // É SUBSET de inputTokens (não somar de novo no total).
        var cachedTokens = (int)(usage?.CachedInputTokenCount ?? 0);

        activity?.SetTag("tokens.input", inputTokens);
        activity?.SetTag("tokens.output", outputTokens);
        activity?.SetTag("tokens.total", totalTokens);
        activity?.SetTag("tokens.cached", cachedTokens);
        activity?.SetTag("duration.ms", durationMs);

        if (totalTokens <= 0) return;

        MetricsRegistry.AgentTokensUsed.Record(totalTokens,
            new KeyValuePair<string, object?>("agent_id", _agentId),
            new KeyValuePair<string, object?>("model_id", _modelId));

        _logger.LogInformation(
            "[TokenUsage] Agent={AgentId} Model={ModelId} Input={InputTokens} Output={OutputTokens} Total={TotalTokens} Duration={DurationMs:F0}ms",
            _agentId, _modelId, inputTokens, outputTokens, totalTokens, durationMs);

        var ctx = DelegateExecutor.Current.Value;
        var executionId = ctx?.ExecutionId;
        var workflowId = ctx?.WorkflowId;

        // Acumula no budget da execução — será checado na próxima chamada LLM via EnforceBudget.
        ctx?.Budget?.Add(totalTokens);

        // Calcula custo incremental em USD e acumula no budget.
        // Fire-and-forget via Task.Run impediria enforcement na chamada seguinte → fazemos síncrono.
        if (ctx?.Budget is not null && _pricingCache is not null)
        {
            try
            {
                var pricing = _pricingCache.GetAsync(_modelId).AsTask().GetAwaiter().GetResult();
                if (pricing is not null)
                {
                    var cost =
                        inputTokens * pricing.PricePerInputToken +
                        outputTokens * pricing.PricePerOutputToken;
                    var newTotal = ctx.Budget.AddCost(cost);
                    activity?.SetTag("cost.usd.delta", (double)cost);
                    activity?.SetTag("cost.usd.total", (double)newTotal);
                    MetricsRegistry.AgentCostUsd.Record((double)cost,
                        new KeyValuePair<string, object?>("agent_id", _agentId),
                        new KeyValuePair<string, object?>("model_id", _modelId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[TokenTrackingChatClient] Falha ao calcular custo USD para '{Model}'.", _modelId);
            }
        }

        var promptVersionId = ctx?.PromptVersions.GetValueOrDefault(_agentId);

        // Experiment binding: composer grava quando bucketing acontece;
        // aqui só copiamos pra persistir em llm_token_usage pra análise.
        int? experimentId = null;
        char? experimentVariant = null;
        if (ctx?.ExperimentAssignments is { } assignments
            && assignments.TryGetValue(_agentId, out var assignment))
        {
            experimentId = assignment.ExperimentId;
            experimentVariant = assignment.Variant;
        }

        _usageWriter.TryWrite(new LlmTokenUsage
        {
            AgentId = _agentId,
            ModelId = _modelId,
            ExecutionId = executionId,
            WorkflowId = workflowId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CachedTokens = cachedTokens,
            ProjectId = ctx?.ProjectId,
            DurationMs = durationMs,
            PromptVersionId = promptVersionId,
            OutputContent = outputContent,
            CreatedAt = DateTime.UtcNow,
            ExperimentId = experimentId,
            ExperimentVariant = experimentVariant,
        });
    }
}
