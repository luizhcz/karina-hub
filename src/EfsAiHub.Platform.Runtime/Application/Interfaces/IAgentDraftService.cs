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
    /// Submete o draft ao painel de aprovação (Draft|Rejected → PendingApproval).
    /// Ao aprovar, agent vai pra agent_definitions; rejeitado volta com feedback.
    /// Owner gate: só projeto dono submete.
    /// </summary>
    Task<AgentDraft> SubmitForApprovalAsync(
        string id,
        string actorUserId,
        CancellationToken ct = default);
}
