namespace EfsAiHub.Core.Agents.Capture;

/// <summary>
/// Estado da captura de prompts LLM. Single row em
/// <c>aihub.llm_capture_config</c>. Cache Redis lê esse record com TTL curto
/// (~30s) — middleware <c>LlmInvocationCaptureChatClient</c> consulta a cada
/// chamada antes de decidir capturar.
///
/// Quando <see cref="Enabled"/>=false OU <see cref="ExpiresAt"/> &lt; now,
/// captura bypassa. Scope filters são "match-any" — se <see cref="ProjectIds"/>
/// é null/vazio, casa qualquer projeto; senão, captura só quando ProjectId do
/// turno está na lista. Mesma regra pra AgentIds e WorkflowIds (AND entre
/// dimensões).
/// </summary>
public sealed record LlmCaptureConfig(
    bool Enabled,
    IReadOnlyList<string>? ProjectIds,
    IReadOnlyList<string>? AgentIds,
    IReadOnlyList<string>? WorkflowIds,
    DateTime? ExpiresAt,
    string? EnabledBy,
    DateTime? EnabledAt,
    DateTime UpdatedAt)
{
    public static LlmCaptureConfig Disabled() => new(
        Enabled: false,
        ProjectIds: null,
        AgentIds: null,
        WorkflowIds: null,
        ExpiresAt: null,
        EnabledBy: null,
        EnabledAt: null,
        UpdatedAt: DateTime.UtcNow);

    /// <summary>True quando captura deveria estar ATIVA agora (Enabled + não
    /// expirou). Não checa scope — caller faz match contra projeto/agent/wf.</summary>
    public bool IsLive(DateTime? nowUtc = null)
    {
        if (!Enabled) return false;
        if (ExpiresAt is { } expires && expires <= (nowUtc ?? DateTime.UtcNow)) return false;
        return true;
    }

    /// <summary>True quando captura deve rodar pra esse contexto específico.
    /// Combina <see cref="IsLive"/> + match dos scope filters.</summary>
    public bool Matches(string? projectId, string? agentId, string? workflowId, DateTime? nowUtc = null)
    {
        if (!IsLive(nowUtc)) return false;
        if (ProjectIds is { Count: > 0 } && (projectId is null || !ProjectIds.Contains(projectId, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (AgentIds is { Count: > 0 } && (agentId is null || !AgentIds.Contains(agentId, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (WorkflowIds is { Count: > 0 } && (workflowId is null || !WorkflowIds.Contains(workflowId, StringComparer.OrdinalIgnoreCase)))
            return false;
        return true;
    }
}

public interface ILlmCaptureConfigRepository
{
    /// <summary>Lê estado atual. Retorna <see cref="LlmCaptureConfig.Disabled"/>
    /// quando a row singleton ainda não foi inicializada.</summary>
    Task<LlmCaptureConfig> GetAsync(CancellationToken ct = default);

    /// <summary>Sobrescreve estado atual. UPSERT (Id=1 fixo).</summary>
    Task<LlmCaptureConfig> UpsertAsync(LlmCaptureConfig config, CancellationToken ct = default);
}
