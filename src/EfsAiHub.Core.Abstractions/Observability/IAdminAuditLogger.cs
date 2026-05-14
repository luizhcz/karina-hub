using System.Text.Json;

namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Registra mudanças CRUD em recursos administrativos (Project, Agent, Workflow, Skill,
/// ModelPricing). Fire-and-log: chamadas não devem quebrar o request path — falhas
/// de escrita são logadas como warning e engolidas (auditoria é secundária ao fluxo).
/// </summary>
public interface IAdminAuditLogger
{
    /// <summary>
    /// Persiste um evento de auditoria. Normalmente chamado <b>depois</b> do persist
    /// do recurso principal, para não gerar linhas órfãs em caso de falha do write primário.
    /// </summary>
    Task RecordAsync(AdminAuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Consulta paginada por tenant + filtros opcionais. Ordenação: Timestamp DESC
    /// (mais recente primeiro) — espelha o índice IX_admin_audit_log_TenantId_Timestamp.
    /// </summary>
    Task<IReadOnlyList<AdminAuditEntry>> QueryAsync(AdminAuditQuery query, CancellationToken ct = default);

    /// <summary>Conta total de linhas que batem o filtro (sem limit/offset).</summary>
    Task<int> CountAsync(AdminAuditQuery query, CancellationToken ct = default);
}

/// <summary>
/// Entry de auditoria imutável. Campos PayloadBefore/After são opcionais — em creates
/// só há After; em deletes só há Before; em updates preferimos ambos para o diff.
/// </summary>
public sealed class AdminAuditEntry
{
    public long Id { get; init; }
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public required string ActorUserId { get; init; }
    public string? ActorUserType { get; init; }
    public required string Action { get; init; }         // create | update | delete
    public required string ResourceType { get; init; }   // project | agent | workflow | skill | model_pricing
    public required string ResourceId { get; init; }
    public JsonDocument? PayloadBefore { get; init; }
    public JsonDocument? PayloadAfter { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>Filtros para paginação em /api/aihub/admin/audit-log.</summary>
public sealed class AdminAuditQuery
{
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? ActorUserId { get; init; }
    public string? Action { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Constantes para Action e ResourceType — evita typos nos 15+ call sites dos controllers.
/// </summary>
public static class AdminAuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";

    /// <summary>
    /// Leitura administrativa de recurso sensível. Uso seletivo — NÃO
    /// instrumentar todos os GETs (overhead). Começou pela feature persona
    /// (LGPD art. 37 pede trilha de consulta).
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// Violação detectada pelo BlocklistChatClient (PR Blocklist Guardrail v1).
    /// PayloadAfter contém violation_id, pattern_id, category, action, content_hash,
    /// context_obfuscated — nunca o conteúdo cru.
    /// </summary>
    public const string BlocklistViolation = "blocklist_violation";

    /// <summary>
    /// Mudança de visibilidade ('project' → 'global' ou vice-versa) em
    /// WorkflowDefinition. PayloadBefore/After mínimos — apenas {visibility}.
    /// Emitido pelo PATCH /api/aihub/workflows/{id}/visibility.
    /// </summary>
    public const string WorkflowVisibilityChanged = "workflow.visibility_changed";

    /// <summary>
    /// Mudança de visibilidade em AgentDefinition (project ↔ global).
    /// Emitido pelo PATCH /api/aihub/agents/{id}/visibility.
    /// </summary>
    public const string AgentVisibilityChanged = "agent.visibility_changed";

    /// <summary>
    /// Liga/desliga o agent (Enabled true ↔ false). Emitido pelo PATCH
    /// /api/aihub/agents/{id}/enabled. Quando Enabled=false, AgentFactory pula o agent
    /// em runtime (workflows continuam mas sem invocá-lo).
    /// </summary>
    public const string AgentEnabledChanged = "agent.enabled_changed";

    /// <summary>
    /// Criação de rascunho de agent. Emitido pelo POST /api/aihub/agent-drafts.
    /// PayloadAfter inclui draftId, isEditDraft, baseAgentId.
    /// </summary>
    public const string AgentDraftCreated = "agent.draft_created";

    /// <summary>
    /// Atualização de rascunho. Emitido pelo PUT /api/aihub/agent-drafts/{id}.
    /// PayloadAfter inclui draftId, updatedAt.
    /// </summary>
    public const string AgentDraftUpdated = "agent.draft_updated";

    /// <summary>
    /// Descarte explícito de rascunho. Emitido pelo DELETE /api/aihub/agent-drafts/{id}.
    /// </summary>
    public const string AgentDraftDeleted = "agent.draft_deleted";

    /// <summary>
    /// Submissão de rascunho ao painel de aprovação. Emitido pelo POST
    /// /api/aihub/agent-drafts/{id}/submit (transição Draft|Rejected → PendingApproval).
    /// PayloadAfter inclui draftId, isEditDraft, baseAgentId, wasResubmit.
    /// </summary>
    public const string AgentDraftSubmitted = "agent.draft_submitted";

    /// <summary>
    /// Aprovação de rascunho pelo painel. Emitido pelo POST
    /// /api/aihub/agent-approvals/{id}/approve após promoção atômica (agent_definitions
    /// + agent_versions + delete do draft + history entry). PayloadAfter inclui
    /// agentId, fromDraftId, wasEditDraft, approverUserId, ageHours.
    /// </summary>
    public const string AgentDraftApproved = "agent.draft_approved";

    /// <summary>
    /// Rejeição de rascunho pelo painel. Emitido pelo POST
    /// /api/aihub/agent-approvals/{id}/reject. PayloadAfter inclui draftId,
    /// approverUserId, feedback, ageHours.
    /// </summary>
    public const string AgentDraftRejected = "agent.draft_rejected";

    /// <summary>
    /// Workflow do projeto X resolveu agent global do projeto Y.
    /// Evento operacional emitido em cada execução cross-project pelo AgentFactory.
    /// PayloadAfter inclui callerProjectId, ownerProjectId, workflowId, agentId.
    /// O AgentFactory aplica throttle (LRU 60s) pra evitar inflar audit em loops.
    /// </summary>
    public const string CrossProjectInvoke = "cross_project_invoke";

    /// <summary>
    /// Publicação de uma nova AgentVersion (snapshot imutável). Emitido pelo
    /// AgentService.PublishVersionAsync após persistência via AppendAsync.
    /// PayloadAfter inclui revision, breakingChange, contentHash. Quando idempotência
    /// por ContentHash retorna existing version (no-op), o audit NÃO é emitido.
    /// </summary>
    public const string AgentVersionPublished = "agent.version_published";

    /// <summary>
    /// Falha de roundtrip lossless durante deserialização de AgentVersion — snapshot
    /// JSON corrompido ou ausente força fallback defensivo na PgAgentVersionRepository.
    /// Severidade: alta (sev1) — workflows pinados podem executar com defaults
    /// inseguros. PayloadAfter inclui agentVersionId, agentDefinitionId, contentHash.
    /// A métrica <c>agents.version_lossless_roundtrip_failures_total</c> dispara hoje no
    /// Deserialize (estático, sem auditLogger acessível). A emissão da audit fica
    /// reservada pra dispatcher fora do hot path da deserialização — caller que detecte
    /// falha via métrica e queira persistir contexto adicional.
    /// </summary>
    public const string AgentVersionLosslessRoundtripFailed = "agent.version_lossless_roundtrip_failed";

    /// <summary>
    /// Atualização manual de pin de agent ref em workflow via UI/API
    /// (PATCH /api/aihub/workflows/{id}/agents/{agentId}/pin). Emitido em toda
    /// transição de pin manual (incluindo casos onde caller atualiza pra
    /// version mais nova após receber notification de breaking).
    /// PayloadAfter inclui agentId, previousVersionId, newVersionId, wasBreaking, reason.
    /// </summary>
    public const string WorkflowAgentVersionPinned = "workflow.agent_version_pinned";

    /// <summary>
    /// Session de teste isolado de Conversational criada via POST
    /// /api/aihub/agents/{id}/sandbox-sessions (com Mode=chat derivado pelo
    /// backend). PayloadAfter inclui chatSandboxSessionId, agentId,
    /// agentVersionId, mode, workflowId, conversationId.
    /// </summary>
    public const string ChatSandboxSessionCreated = "chat_sandbox.session_created";

    /// <summary>
    /// Validação manual de uma session (admin marca o Conversational como apto
    /// pra plug em chats de produção). Popula colunas LastChatSandboxValidated*
    /// em agent_definitions. PayloadAfter inclui agentId, agentVersionId,
    /// chatSandboxSessionId, notes.
    /// </summary>
    public const string ChatSandboxValidated = "chat_sandbox.validated";

    /// <summary>
    /// Validação anterior do agent foi invalidada porque uma nova
    /// AgentVersion foi publicada. Emitido pelo approval flow ao zerar as
    /// colunas LastChatSandboxValidated*. PayloadAfter inclui agentId,
    /// previousValidatedAgentVersionId, newAgentVersionId.
    /// </summary>
    public const string ChatSandboxValidationInvalidated = "chat_sandbox.validation_invalidated";

    /// <summary>
    /// Session Standalone Sandbox criada pra testar agente Custom/Worker/ToolRunner
    /// sem deploy permanente. Emitido pelo POST
    /// /api/aihub/agents/{id}/sandbox-sessions quando agent.Type ≠ Conversational.
    /// PayloadAfter inclui sandboxSessionId, agentId, agentType, agentVersionId,
    /// mode, workflowId. <strong>Gate de validation não se aplica</strong>
    /// (Standalone não tem cadeia de consumo downstream).
    /// </summary>
    public const string AgentSandboxStandaloneSessionCreated = "agent_sandbox.standalone_session_created";

    /// <summary>
    /// Predição isolada de intent por agente Router. Emitido pelo POST
    /// /api/aihub/agents/{id}/predict-intent. PayloadAfter inclui agentId,
    /// agentVersionId, inputLength, intent, latencyMs.
    /// </summary>
    public const string RouterIntentPredicted = "router.intent_predicted";

    /// <summary>
    /// Criação de Predefined Model (preset global). Emitido pelo
    /// POST /api/aihub/admin/predefined-models. PayloadAfter inclui id, displayName,
    /// provider, deploymentName.
    /// </summary>
    public const string PredefinedModelCreated = "predefined_model.created";

    /// <summary>
    /// Atualização de Predefined Model. Emitido pelo
    /// PUT /api/aihub/admin/predefined-models/{id}. PayloadAfter inclui id, updatedAt.
    /// </summary>
    public const string PredefinedModelUpdated = "predefined_model.updated";

    /// <summary>
    /// Remoção de Predefined Model. Emitido pelo
    /// DELETE /api/aihub/admin/predefined-models/{id}. Agents que referenciam o preset
    /// não conseguirão invocar até serem reapontados ou re-seedados.
    /// </summary>
    public const string PredefinedModelDeleted = "predefined_model.deleted";

    /// <summary>
    /// Criação de Generic Tool (HTTP genérica). Emitido pelo POST /api/aihub/generic-tools.
    /// PayloadAfter inclui toolId, name, httpMethod, projectId.
    /// </summary>
    public const string GenericToolCreated = "generic_tool.created";

    /// <summary>
    /// Atualização de Generic Tool. Emitido pelo PUT /api/aihub/generic-tools/{id}.
    /// PayloadAfter inclui toolId, updatedAt.
    /// </summary>
    public const string GenericToolUpdated = "generic_tool.updated";

    /// <summary>
    /// Descarte explícito de Generic Tool. Emitido pelo DELETE /api/aihub/generic-tools/{id}.
    /// </summary>
    public const string GenericToolDeleted = "generic_tool.deleted";
}

public static class AdminAuditResources
{
    public const string Project = "project";
    public const string Agent = "agent";
    public const string Workflow = "workflow";
    public const string Skill = "skill";
    public const string ModelPricing = "model_pricing";
    public const string McpServer = "mcp_server";
    public const string DocumentIntelligencePricing = "document_intelligence_pricing";
    public const string PersonaCache = "persona_cache";
    public const string PersonaPromptTemplate = "persona_prompt_template";
    public const string PersonaPromptExperiment = "persona_prompt_experiment";

    /// <summary>Recurso virtual representando a config de blocklist do projeto.</summary>
    public const string Blocklist = "blocklist";

    /// <summary>Tool HTTP genérica cadastrada por projeto.</summary>
    public const string GenericTool = "generic_tool";

    /// <summary>Catálogo global de presets de modelo (provider+deployment+defaults).</summary>
    public const string PredefinedModel = "predefined_model";

    /// <summary>Item do pool global de intents (cross-project por tenant) consumido por Router agents.</summary>
    public const string RouterIntent = "router_intent";

    /// <summary>Session de teste isolado de agente Conversational em chat AG-UI.</summary>
    public const string ChatSandboxSession = "chat_sandbox_session";

    /// <summary>
    /// Session de Standalone Sandbox (Custom/Worker/ToolRunner). Resource type
    /// distinto de <see cref="ChatSandboxSession"/> pra que queries SQL filtrem
    /// por origem (chat vs standalone) sem precisar parsear PayloadAfter.
    /// </summary>
    public const string AgentSandboxSession = "agent_sandbox_session";
}

/// <summary>
/// Tipos de actor canônicos. "human" cobre usuários reais, "agent" cobre ações
/// disparadas pelo runtime (ex: BlocklistChatClient), "system" cobre tarefas
/// agendadas/background sem usuário associado.
/// </summary>
public static class AdminAuditActorTypes
{
    public const string Human = "human";
    public const string Agent = "agent";
    public const string System = "system";
}
