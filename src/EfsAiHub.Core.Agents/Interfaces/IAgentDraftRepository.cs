namespace EfsAiHub.Core.Agents;

/// <summary>
/// Estado mutável intermediário antes do agent virar canônico em
/// <c>agent_definitions</c>. Owner-scoped (project-only) por default; queries
/// do painel de aprovação usam <see cref="ListByStatusForTenantAsync"/> com
/// bypass de query filter pra ver drafts pending de qualquer project do tenant.
/// Workflows nunca consultam essa tabela.
/// </summary>
public interface IAgentDraftRepository
{
    Task<AgentDraft?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Lê draft pelo Id sem aplicar query filter de project. Usado pelo painel
    /// de aprovação que opera tenant-scope. Filtra por TenantId atual.
    /// </summary>
    Task<AgentDraft?> GetByIdForTenantAsync(string id, CancellationToken ct = default);

    /// <summary>Lista drafts do project atual (respeita query filter).</summary>
    Task<IReadOnlyList<AgentDraft>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Lista drafts do tenant atual com status filtrado, bypassando query filter
    /// de project. Alimenta o painel de aprovação (cross-project, tenant-scope).
    /// </summary>
    Task<IReadOnlyList<AgentDraft>> ListByStatusForTenantAsync(
        AgentDraftStatus status,
        CancellationToken ct = default);

    /// <summary>True se já existe draft com esse Id (sem materializar payload).</summary>
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Insere ou atualiza o draft. Quando <paramref name="expectedUpdatedAt"/> é
    /// fornecido (PUT), aplica optimistic concurrency: WHERE Id=@id AND UpdatedAt=@expected
    /// — 0 rows afetadas lança <see cref="DraftConcurrencyException"/>.
    /// PUT em draft com Status=PendingApproval é rejeitado (lança
    /// <see cref="InvalidOperationException"/>) — owner deve cancelar a submissão antes.
    /// PUT em draft com Status=Rejected limpa <see cref="AgentDraft.RejectionFeedback"/>
    /// e volta status para Draft (re-edição implícita).
    /// </summary>
    Task<AgentDraft> UpsertAsync(
        AgentDraft draft,
        DateTime? expectedUpdatedAt,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Transição Draft|Rejected → PendingApproval. Atomico via WHERE Status IN
    /// ('Draft','Rejected') — concorrência detectada com 0 rows. Stamp SubmittedAt
    /// e escreve entry no agent_approval_history (action=Submitted ou Resubmitted
    /// se vinha de Rejected).
    /// </summary>
    Task<AgentDraft> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Transição PendingApproval → agent_definitions. Detecta race com re-publish
    /// concorrente do agent base (edit-draft) via BaseRevision. Cria AgentVersion
    /// snapshot, deleta draft, escreve entry no history (action=Approved).
    /// </summary>
    Task<AgentDefinition> ApproveAsync(
        string id,
        string actorUserId,
        string? changeReason,
        CancellationToken ct = default);

    /// <summary>
    /// Transição PendingApproval → Rejected. Persiste feedback e escreve history
    /// (action=Rejected). Atomico via WHERE Status='PendingApproval' — race com
    /// outro aprovador ou cancelamento detectada.
    /// </summary>
    Task<AgentDraft> RejectAsync(
        string id,
        string actorUserId,
        string feedback,
        CancellationToken ct = default);

    /// <summary>Lista history de transições do draft (ordenado por OccurredAt asc).</summary>
    Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetHistoryAsync(
        string draftId,
        CancellationToken ct = default);
}

/// <summary>
/// Domain object de draft — espelha <see cref="AgentDefinition"/> em estrutura mas
/// permite estado incompleto via <see cref="AgentDraftPayload"/>.
/// </summary>
public sealed class AgentDraft
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required AgentDraftPayload Payload { get; set; }
    public required string ProjectId { get; init; }
    public required string TenantId { get; set; }

    /// <summary>Quando setado, o draft é fork (edit-draft) do agent publicado com esse Id.</summary>
    public string? BaseAgentId { get; init; }

    /// <summary>Revision do agent base no momento do fork — usada pra detectar race de re-publish.</summary>
    public int? BaseRevision { get; init; }

    /// <summary>Estado no approval workflow.</summary>
    public AgentDraftStatus Status { get; set; } = AgentDraftStatus.Draft;

    /// <summary>Mensagem do aprovador quando Status=Rejected. Null em Draft/PendingApproval.</summary>
    public string? RejectionFeedback { get; set; }

    /// <summary>Timestamp da última transição para PendingApproval.</summary>
    public DateTime? SubmittedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; init; }

    public bool IsEditDraft => !string.IsNullOrEmpty(BaseAgentId);
}

public enum AgentDraftStatus
{
    Draft,
    PendingApproval,
    Rejected,
}

/// <summary>Entry imutável do agent_approval_history.</summary>
public sealed record AgentApprovalHistoryEntry(
    string Id,
    string DraftId,
    string TenantId,
    AgentApprovalAction Action,
    string ActorUserId,
    string? Feedback,
    DateTime OccurredAt);

public enum AgentApprovalAction
{
    Submitted,
    Resubmitted,
    Approved,
    Rejected,
}

public sealed class DraftConcurrencyException : Exception
{
    public DraftConcurrencyException(string draftId)
        : base($"Draft '{draftId}' foi modificado por outra requisição (UpdatedAt divergente).")
    { }
}

public sealed class DraftRePublishRaceException : Exception
{
    public string DraftId { get; }
    public int BaseRevision { get; }
    public int CurrentRevision { get; }

    public DraftRePublishRaceException(string draftId, int baseRevision, int currentRevision)
        : base(
            $"Agent '{draftId}' foi re-publicado por outra requisição enquanto o draft estava aberto " +
            $"(base revision {baseRevision} → atual {currentRevision}). Reabra o editor pra incorporar mudanças.")
    {
        DraftId = draftId;
        BaseRevision = baseRevision;
        CurrentRevision = currentRevision;
    }
}

/// <summary>
/// Lançada quando uma transição é tentada num status incompatível (ex: submit
/// num draft que já está PendingApproval, ou approve em draft que está Draft).
/// </summary>
public sealed class DraftStatusTransitionException : Exception
{
    public AgentDraftStatus CurrentStatus { get; }
    public string Operation { get; }

    public DraftStatusTransitionException(string draftId, AgentDraftStatus currentStatus, string operation)
        : base($"Operação '{operation}' não é válida pra draft '{draftId}' em status '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        Operation = operation;
    }
}
