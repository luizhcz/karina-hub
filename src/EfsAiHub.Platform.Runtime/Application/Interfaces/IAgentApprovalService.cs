namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Painel de aprovação. Tenant-scope: aprovador enxerga drafts pending de
/// qualquer project do tenant atual (bypass do query filter owner-only). Sem
/// permissions específicas: qualquer caller do tenant pode aprovar/rejeitar
/// (decisão da feature). Audit registra ActorUserId pra rastreabilidade.
/// </summary>
public interface IAgentApprovalService
{
    /// <summary>Lista drafts em <see cref="AgentDraftStatus.PendingApproval"/> do tenant atual.</summary>
    Task<IReadOnlyList<AgentDraft>> ListPendingAsync(CancellationToken ct = default);

    /// <summary>Lista drafts num status específico do tenant atual.</summary>
    Task<IReadOnlyList<AgentDraft>> ListByStatusAsync(AgentDraftStatus status, CancellationToken ct = default);

    /// <summary>Detalhe completo do draft (tenant-scope).</summary>
    Task<AgentDraft?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Promove o draft a agent canônico. Detecta race com re-publish concorrente
    /// do agent base (edit-draft) via BaseRevision. Retorna o agent publicado.
    /// </summary>
    Task<AgentDefinition> ApproveAsync(
        string id,
        string actorUserId,
        string? changeReason,
        CancellationToken ct = default);

    /// <summary>
    /// Rejeita o draft com feedback obrigatório. Owner re-edita ou re-submete.
    /// </summary>
    Task<AgentDraft> RejectAsync(
        string id,
        string actorUserId,
        string feedback,
        CancellationToken ct = default);

    /// <summary>Lista history de transições do draft.</summary>
    Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetHistoryAsync(string draftId, CancellationToken ct = default);
}
