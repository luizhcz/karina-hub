namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Aplicação acima de <see cref="IAgentDraftRepository"/>. Aplica owner gate,
/// emite audit/métricas, gera Id quando ausente, e orquestra fork em edit-draft
/// (snapshot do agent base no momento do POST /edit-draft). Rascunho promovido a
/// agent canônico via painel de aprovação (<see cref="IAgentApprovalService"/>).
/// </summary>
public interface IAgentDraftService
{
    Task<AgentDraft> CreateAsync(
        string? id,
        AgentDraftPayload payload,
        CancellationToken ct = default);

    /// <summary>
    /// Cria um draft fork-eando um agent já publicado. <paramref name="baseAgentId"/>
    /// referência o agent canônico — original permanece intacto até aprovação.
    /// Rejeita se já existe draft com mesmo Id (409). Owner-gated: só projeto dono do
    /// agent pode forkar.
    /// </summary>
    Task<AgentDraft> CreateEditDraftAsync(string baseAgentId, CancellationToken ct = default);

    Task<AgentDraft?> GetAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<AgentDraft>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Atualiza o draft com optimistic concurrency. <paramref name="expectedUpdatedAt"/>
    /// vem do GET anterior — divergência lança <see cref="DraftConcurrencyException"/>.
    /// Edição em draft com Status=PendingApproval lança <see cref="DraftStatusTransitionException"/>
    /// (owner cancela submissão antes). Edição em Rejected limpa feedback e volta status pra Draft.
    /// </summary>
    Task<AgentDraft> UpdateAsync(
        string id,
        AgentDraftPayload payload,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Submete o draft ao painel de aprovação. Comportamento depende do
    /// resultado do <c>AgentChangeClassifier</c> em edit-drafts:
    /// <list type="bullet">
    ///   <item><b>New draft</b> (isEditDraft=false): sempre Draft|Rejected → PendingApproval.</item>
    ///   <item><b>Edit-draft cosmético</b> (só Description/Metadata mudaram):
    ///         <see cref="SubmitForApprovalResult.AutoApproved"/> — pula a fila,
    ///         publica direto e escreve <c>AutoApproved</c> no history.</item>
    ///   <item><b>Edit-draft comportamental</b>: vai pra PendingApproval normal.</item>
    /// </list>
    /// Owner gate: só projeto dono submete.
    /// </summary>
    Task<SubmitForApprovalResult> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Retorna a trilha de approval unificada de um agent publicado — drafts
    /// passados (Submitted/Approved/Rejected/AutoApproved) + AdminOverride
    /// aplicado via PUT direto. Ordem por OccurredAt asc.
    /// </summary>
    Task<IReadOnlyList<AgentApprovalHistoryEntry>> GetApprovalHistoryByAgentAsync(
        string agentDefinitionId,
        CancellationToken ct = default);

    /// <summary>
    /// Registra uma entry de AdminOverride no history quando admin atualiza
    /// agent direto via PUT /api/agents/{id}. Mantém audit unificado.
    /// </summary>
    Task AppendAdminOverrideAsync(
        string agentDefinitionId,
        string actorUserId,
        string changeReason,
        CancellationToken ct = default);
}

/// <summary>
/// Resultado da submissão de um draft. Em edit-drafts cosméticos o sistema
/// aprova automaticamente — o caller (controller/UI) usa esse retorno pra
/// sinalizar a diferença pro usuário ("publicado" vs "aguardando aprovação").
/// </summary>
public sealed record SubmitForApprovalResult(
    AgentDraft Draft,
    bool AutoApproved,
    AgentChangeTier? Tier);
