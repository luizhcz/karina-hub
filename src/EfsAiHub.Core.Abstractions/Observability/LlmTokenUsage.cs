namespace EfsAiHub.Core.Abstractions.Observability;

public class LlmTokenUsage
{
    public long Id { get; set; }
    public string AgentId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string? ExecutionId { get; set; }
    public string? WorkflowId { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }

    /// <summary>
    /// Tokens de input que bateram no cache da OpenAI (prompt caching). 0 se
    /// não houve cache hit ou se o modelo não suporta. Capturado via
    /// <c>UsageDetails.CachedInputTokenCount</c> em <c>TokenTrackingChatClient</c>.
    /// Incluído em <see cref="InputTokens"/> (não é aditivo).
    /// </summary>
    public int CachedTokens { get; set; }

    /// <summary>
    /// Project que originou a chamada LLM. Nullable pra compat com rows
    /// pré-F4; writers novos populam via <c>IProjectContextAccessor</c>.
    /// Permite analytics cross-project e HasQueryFilter no DbContext.
    /// </summary>
    public string? ProjectId { get; set; }
    public double DurationMs { get; set; }
    public string? PromptVersionId { get; set; }
    public string? AgentVersionId { get; set; }
    public string? OutputContent { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// F6 — id do experiment A/B ativo quando essa LLM call rodou. Null = não
    /// participou de experiment. Permite agregar métricas por variant.
    /// </summary>
    public int? ExperimentId { get; set; }

    /// <summary>
    /// F6 — variant ('A'/'B') atribuída via bucketing determinístico por userId.
    /// Null quando <see cref="ExperimentId"/> é null.
    /// </summary>
    public char? ExperimentVariant { get; set; }
}
