namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Resposta do endpoint <c>GET /api/workflows/{id}/agent-version-status</c>.
/// Resume estado de pin de cada agent ref pra UI tomar decisões de migration:
/// botão "atualizar pra vN" + diff modal com changeReasons acumuladas.
/// </summary>
public class WorkflowAgentVersionStatusResponse
{
    public required string AgentId { get; init; }
    public string? AgentName { get; init; }

    /// <summary>Pin atual no workflow ref (null = sem pin, comportamento legacy).</summary>
    public string? PinnedVersionId { get; init; }
    public int? PinnedRevision { get; init; }

    /// <summary>Current Published do agent (pode ser null em estado pré-publish).</summary>
    public string? CurrentVersionId { get; init; }
    public int? CurrentRevision { get; init; }

    /// <summary>
    /// true se há AgentVersion com <c>BreakingChange=true</c> entre pinned e current
    /// (workflow não pode propagar automaticamente — caller fica preso ao pin).
    /// false em ausência de breaking, quando pin == current, ou quando ref não tem
    /// pin (legacy — não há "blocked" sem pin pra comparar).
    /// </summary>
    public bool IsPinnedBlockedByBreaking { get; init; }

    /// <summary>
    /// Lista de change reasons das versions entre pinned e current (ordenadas por
    /// revision ASC). Usado pelo diff modal pra mostrar o que mudou desde o pin.
    /// Vazia quando pin == current.
    /// </summary>
    public IReadOnlyList<WorkflowAgentVersionChangeEntry> Changes { get; init; } = [];

    /// <summary>true quando há current.Revision > pinned.Revision (UI mostra badge).</summary>
    public bool HasUpdate { get; init; }
}

/// <summary>Entry individual da lista de mudanças entre pinned e current.</summary>
public class WorkflowAgentVersionChangeEntry
{
    public required string AgentVersionId { get; init; }
    public required int Revision { get; init; }
    public bool? BreakingChange { get; init; }
    public string? ChangeReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
