using System.Diagnostics.Metrics;

namespace EfsAiHub.Infra.Observability;

public static class MetricsRegistry
{
    public const string MeterName = "EfsAiHub.Api";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> WorkflowsTriggered =
        _meter.CreateCounter<long>("workflows.triggered", description: "Total de workflows disparados");

    public static readonly Counter<long> WorkflowsCompleted =
        _meter.CreateCounter<long>("workflows.completed", description: "Total de workflows concluídos com sucesso");

    public static readonly Counter<long> WorkflowsFailed =
        _meter.CreateCounter<long>("workflows.failed",
            description: "Total de workflows que falharam. Tags: workflow.id, error.category " +
                         "(Timeout | BudgetExceeded | HitlRejected | CheckpointRecoveryFailed | FrameworkError | " +
                         "AgentError | ToolError | InvalidConfig | DependencyFailure | CircuitOpen | Cancelled | Unknown). " +
                         "Dashboards devem quebrar por error.category para priorizar tipo de falha.");

    public static readonly Counter<long> WorkflowsCancelled =
        _meter.CreateCounter<long>("workflows.cancelled", description: "Total de workflows cancelados");

    public static readonly Histogram<double> WorkflowDurationMs =
        _meter.CreateHistogram<double>("workflows.duration_ms", unit: "ms",
            description: "Duração de execução dos workflows em milissegundos");

    public static readonly Histogram<double> AgentTokensUsed =
        _meter.CreateHistogram<double>("agents.tokens_used",
            description: "Tokens utilizados por execução de agente");

    /// <summary>Custo incremental em USD por chamada LLM.</summary>
    public static readonly Histogram<double> AgentCostUsd =
        _meter.CreateHistogram<double>("agents.cost_usd",
            description: "Custo em USD por chamada LLM (incremental, calculado via ModelPricing)");

    public static readonly UpDownCounter<int> ActiveExecutions =
        _meter.CreateUpDownCounter<int>("workflows.active_executions",
            description: "Número de execuções ativas no momento");

    public static readonly Histogram<double> AgentInvocationDuration =
        _meter.CreateHistogram<double>("agent.invocation.duration", unit: "s",
            description: "Duração total de invocação de agente (LLM + tools + overhead)");

    public static readonly Counter<long> LlmRetries =
        _meter.CreateCounter<long>("llm.retries",
            description: "Retries de chamadas LLM por erro transiente (429/5xx)");

    public static readonly UpDownCounter<int> ChatActiveExecutions =
        _meter.CreateUpDownCounter<int>("chat.active_executions",
            description: "Execuções Chat Path ativas (bypass de fila)");

    public static readonly Counter<long> ChatBackPressureRejections =
        _meter.CreateCounter<long>("chat.backpressure.rejections",
            description: "Requests Chat rejeitados por back-pressure (HTTP 429)");

    public static readonly Counter<long> ToolAccountOverrides =
        _meter.CreateCounter<long>("tool.account.overrides",
            description: "Tool calls onde o argumento conta/account foi reescrito pelo AccountGuard (ClientLocked)");

    public static readonly Counter<long> ToolAccountOutputAnomaly =
        _meter.CreateCounter<long>("tool.account.output_anomaly",
            description: "Anomalias detectadas pelo AccountGuardChatClient no output final do LLM");

    public static readonly Counter<long> BudgetExceededWarnings =
        _meter.CreateCounter<long>("llm.budget.exceeded",
            description: "Execuções que ultrapassaram budget cap (warning-only — não bloqueia). Tags: scope=workflow|agent|project, cause=cost|tokens.");

    /// <summary>
    /// Contador incrementado quando admin troca a visibilidade de um workflow.
    /// Tags: from=project|global, to=project|global, tenant=&lt;tenantId&gt;.
    /// </summary>
    public static readonly Counter<long> WorkflowVisibilityChanges =
        _meter.CreateCounter<long>("workflows.visibility_changes_total",
            description: "Mudanças de Visibility em WorkflowDefinition. Tags: from, to, tenant.");

    /// <summary>
    /// Contador de mudanças de Visibility em AgentDefinition.
    /// Tags: from=project|global, to=project|global, tenant.
    /// </summary>
    public static readonly Counter<long> AgentVisibilityChanges =
        _meter.CreateCounter<long>("agents.visibility_changes_total",
            description: "Mudanças de Visibility em AgentDefinition. Tags: from, to, tenant.");

    /// <summary>
    /// Contador de execuções onde workflow caller resolveu agent global de outro projeto.
    /// Tags: caller_project, owner_project, tenant. Cuidado de cardinalidade em deploys com 100+ projetos.
    /// </summary>
    public static readonly Counter<long> AgentCrossProjectInvocations =
        _meter.CreateCounter<long>("agents.cross_project_invocations_total",
            description: "Execuções cross-project de agents globais. Tags: caller_project, owner_project, tenant.");

    /// <summary>
    /// Contador de tentativas bloqueadas pela whitelist (AllowedProjectIds).
    /// Tags: caller_project, owner_project, agent_id. Útil pra detectar configurações
    /// erradas (workflow referencia agent que não tá liberado).
    /// </summary>
    public static readonly Counter<long> AgentWhitelistBlocked =
        _meter.CreateCounter<long>("agents.whitelist_blocked_total",
            description: "Resoluções de agent bloqueadas pela whitelist. Tags: caller_project, owner_project, agent_id.");

    /// <summary>
    /// Eviction counter pra in-memory LRU usado em throttle de cross_project_invoke.
    /// Esse counter dispara quando a LRU enche e descarta entries — indica throttle saturado.
    /// </summary>
    public static readonly Counter<long> AuditThrottleLruEvictions =
        _meter.CreateCounter<long>("audit.throttle_lru_evictions_total",
            description: "Entries despejadas da LRU de throttle de audit cross-project.");

/// <summary>
    /// Contador de resoluções de pin de AgentVersion. Tags:
    /// strategy=exact (snapshot pinado retornado), propagated (current adotado por
    /// patch propagation), no_pin_unexpected (ref sem pin atinge runtime — divergência
    /// já que validator sempre exige pin no save).
    /// </summary>
    public static readonly Counter<long> AgentVersionPinResolutions =
        _meter.CreateCounter<long>("agents.version_pin_resolutions_total",
            description: "Resoluções de pin de AgentVersion em workflows. Tags: strategy.");

    /// <summary>
    /// Contador de fallbacks por governance source ausente — pin existe mas o
    /// agent_definitions row sumiu (orphan). Sinaliza inconsistência operacional
    /// e alimenta health check de orphan pins.
    /// </summary>
    public static readonly Counter<long> AgentVersionGovernanceMissing =
        _meter.CreateCounter<long>("agents.version_lossless_governance_missing_total",
            description: "Pin com governance source ausente (orphan). Tags: agent_id.");

    /// <summary>
    /// Contador de falhas de roundtrip lossless em AgentVersion — snapshot JSON
    /// corrompido força o path de fallback defensivo no Deserialize. Severidade
    /// sev1: workflows pinados podem executar com defaults inseguros até re-publish.
    /// Tag: agent_version_id pra trace.
    /// </summary>
    public static readonly Counter<long> AgentVersionLosslessRoundtripFailures =
        _meter.CreateCounter<long>("agents.version_lossless_roundtrip_failures_total",
            description: "Falhas de deserialização de snapshot AgentVersion (sev1). Tags: agent_version_id.");

    /// <summary>
    /// Contador de invocações puladas por agent estar desabilitado (Enabled=false).
    /// Cada vez que AgentFactory encontra um agent ref de workflow apontando pra agent
    /// disabled e pula a criação. Tags: agent_id, workflow_id.
    /// </summary>
    public static readonly Counter<long> AgentDisabledInvocations =
        _meter.CreateCounter<long>("agents.disabled_invocations_total",
            description: "Invocações puladas por agent disabled. Tags: agent_id, workflow_id.");

    /// <summary>
    /// Contador de mudanças de Enabled em AgentDefinition (toggle on↔off).
    /// Tags: from (true|false), to (true|false), tenant.
    /// </summary>
    public static readonly Counter<long> AgentEnabledChanges =
        _meter.CreateCounter<long>("agents.enabled_changes_total",
            description: "Mudanças de Enabled em AgentDefinition. Tags: from, to, tenant.");

    /// <summary>
    /// Drafts de agent criados via POST /api/aihub/agent-drafts. Tags: tenant, is_edit_draft.
    /// </summary>
    public static readonly Counter<long> AgentDraftsCreated =
        _meter.CreateCounter<long>("agents.drafts_created_total",
            description: "Drafts de agent criados. Tags: tenant, is_edit_draft.");

    /// <summary>
    /// Drafts promovidos a agent canônico via aprovação no painel
    /// (POST /api/aihub/agent-approvals/{id}/approve). Tags: tenant, was_edit_draft.
    /// </summary>
    public static readonly Counter<long> AgentDraftsPublished =
        _meter.CreateCounter<long>("agents.drafts_published_total",
            description: "Drafts promovidos via approval. Tags: tenant, was_edit_draft.");

    /// <summary>
    /// Drafts descartados explicitamente via DELETE /api/aihub/agent-drafts/{id}. Tags: tenant.
    /// </summary>
    public static readonly Counter<long> AgentDraftsAbandoned =
        _meter.CreateCounter<long>("agents.drafts_abandoned_total",
            description: "Drafts descartados. Tags: tenant.");

    /// <summary>
    /// Idade do draft no momento do approve, em horas. Histograma — sinaliza tempo
    /// que rascunhos ficam abertos antes de virar produção (sinal de UX).
    /// </summary>
    public static readonly Histogram<double> AgentDraftAgeHours =
        _meter.CreateHistogram<double>("agents.draft_age_hours",
            unit: "h",
            description: "Idade do draft no approve (CreatedAt → approve).");

    /// <summary>
    /// Rascunhos submetidos ao painel de aprovação. Distingue first-time submit
    /// de re-submit pós-rejeição via tag was_resubmit. Tags: tenant, was_resubmit.
    /// </summary>
    public static readonly Counter<long> AgentDraftsSubmitted =
        _meter.CreateCounter<long>("agents.drafts_submitted_total",
            description: "Drafts enviados ao painel. Tags: tenant, was_resubmit.");

    /// <summary>
    /// Rascunhos rejeitados pelo painel. Tags: tenant.
    /// </summary>
    public static readonly Counter<long> AgentDraftsRejected =
        _meter.CreateCounter<long>("agents.drafts_rejected_total",
            description: "Drafts rejeitados pelo painel. Tags: tenant.");

    /// <summary>
    /// Tempo entre Submitted e a transição final (Approved ou Rejected) em horas.
    /// Sinaliza SLA do painel de aprovação. Tags: tenant, outcome (approved|rejected).
    /// </summary>
    public static readonly Histogram<double> AgentApprovalLatencyHours =
        _meter.CreateHistogram<double>("agents.approval_latency_hours",
            unit: "h",
            description: "Latência do painel: SubmittedAt → resolução. Tags: tenant, outcome.");

    public static readonly Counter<long> StaleExecutionCompletionSkipped =
        _meter.CreateCounter<long>("chat.stale_completion.skipped",
            description: "Completions ignoradas por corresponderem a execução não mais ativa na conversa");

    public static readonly Counter<long> RobotMessagesPersisted =
        _meter.CreateCounter<long>("chat.robot_messages.persisted",
            description: "Mensagens com actor=robot registradas via short-circuit (sem disparar workflow). Ver ADR 0014.");

    public static readonly Counter<long> RouterQuickActionHits =
        _meter.CreateCounter<long>("router.quick_action.hits",
            description: "Mensagens classificadas via Quick Action (bypass do LLM). Dimensões: agent_id, intent.");

    public static readonly Counter<long> HitlRecoveries =
        _meter.CreateCounter<long>("hitl.recoveries",
            description: "Execuções retomadas a partir de checkpoint após restart (HitlRecoveryService)");

    private static long _hitlRecoveryBacklog;
    public static void SetHitlRecoveryBacklog(long value) => Interlocked.Exchange(ref _hitlRecoveryBacklog, value);
    public static readonly ObservableGauge<long> HitlRecoveryBacklog =
        _meter.CreateObservableGauge<long>("hitl.recovery.backlog",
            () => Interlocked.Read(ref _hitlRecoveryBacklog),
            description: "Execuções em Paused aguardando recovery pelo HitlRecoveryService");

    public static readonly Counter<long> HitlOrphanedRecoveries =
        _meter.CreateCounter<long>("hitl.orphaned_recoveries",
            description: "Execuções Paused retomadas com HITL já resolvido (Approved/Rejected) — gap do NOTIFY perdido.");

    public static readonly Counter<long> StuckExecutionsRecovered =
        _meter.CreateCounter<long>("stuck_executions.recovered",
            description: "Total de execuções Running marcadas como Failed por inatividade (StuckExecutionRecoveryService).");

    public static readonly Counter<long> HitlRequested =
        _meter.CreateCounter<long>("hitl.requested",
            description: "Interações HITL criadas. Tags: workflow_id");

    public static readonly Counter<long> HitlResolved =
        _meter.CreateCounter<long>("hitl.resolved",
            description: "Interações HITL resolvidas. Tags: outcome (approved/rejected/expired)");

    public static readonly Counter<long> HitlResolveConflicts =
        _meter.CreateCounter<long>("hitl.resolve_conflicts",
            description: "CAS de resolução HITL perdido para outro caller/pod (race concorrente). " +
                         "Tags: outcome (approved/rejected). Alto volume indica contenção no caminho HITL.");

    public static readonly Histogram<double> HitlResolutionDuration =
        _meter.CreateHistogram<double>("hitl.resolution_duration_seconds", unit: "s",
            description: "Tempo entre criação e resolução de uma interação HITL. Tags: outcome");

    private static long _hitlPendingAgeSeconds;
    public static void SetHitlPendingAgeSeconds(long value) =>
        Interlocked.Exchange(ref _hitlPendingAgeSeconds, value);
    public static readonly ObservableGauge<long> HitlPendingAgeSeconds =
        _meter.CreateObservableGauge<long>("hitl.pending_age_seconds",
            () => Interlocked.Read(ref _hitlPendingAgeSeconds),
            description: "Idade em segundos do HITL pendente mais antigo");

    public static readonly Counter<long> CrossNodeCancelReceived =
        _meter.CreateCounter<long>("crossnode.cancel.received",
            description: "Cancel cross-pod aplicado localmente (Fix #A1).");

    public static readonly Counter<long> CrossNodeHitlResolvedReceived =
        _meter.CreateCounter<long>("crossnode.hitl_resolved.received",
            description: "HITL resolved cross-pod aplicado localmente (Fix #A1).");

    public static readonly Counter<long> PersistenceChannelDropped =
        _meter.CreateCounter<long>("persistence.channel.dropped",
            description: "Itens descartados (DropOldest) por channel de persistência. Tag: channel=token_usage|tool_invocation|node");

    /// <summary>Latência de resolução de AgentVersion (cache+DB).</summary>
    public static readonly Histogram<double> AgentVersionResolveLatency =
        _meter.CreateHistogram<double>("agent.version.resolve_latency", unit: "ms",
            description: "Latência de resolução de AgentVersion (lookup do snapshot corrente)");

    /// <summary>Latência de retrieval RAG por knowledge source.</summary>
    public static readonly Histogram<double> RagRetrievalLatency =
        _meter.CreateHistogram<double>("rag.retrieval.latency", unit: "ms",
            description: "Latência de retrieval RAG. Tag: source_kind=pgvector|foundry|ai_search");

    /// <summary>Documentos retornados por retrieval RAG.</summary>
    public static readonly Histogram<double> RagDocsReturned =
        _meter.CreateHistogram<double>("rag.docs.returned",
            description: "Quantidade de documentos retornados por retrieval RAG");

    /// <summary>Sinais de escalação emitidos (tag: category, routed=true|false).</summary>
    public static readonly Counter<long> EscalationSignalsTotal =
        _meter.CreateCounter<long>("agent.escalation.signals",
            description: "Sinais AgentEscalationSignal emitidos por agentes. Tags: category, routed");

    /// <summary>
    /// Violações de blocklist detectadas. Tags: phase (input|output),
    /// category (PII|SECRETS|FINANCIAL|INTERNAL|CUSTOM), action (Block|Redact|Warn).
    /// project_id deliberadamente fora das tags — high cardinality em SaaS multi-tenant;
    /// breakdown por projeto vai pelo audit log (admin_audit_log.ProjectId).
    /// </summary>
    public static readonly Counter<long> BlocklistViolations =
        _meter.CreateCounter<long>("blocklist.violations",
            description: "Violações detectadas pelo BlocklistChatClient. Tags: phase, category, action");

    /// <summary>
    /// Scans completos (input/output) executados. Numerador da fórmula de taxa de violação
    /// (violations/scans) pra detectar drift de patterns. Tag: phase.
    /// </summary>
    public static readonly Counter<long> BlocklistScans =
        _meter.CreateCounter<long>("blocklist.scans",
            description: "Scans de input/output executados pelo BlocklistChatClient. Tag: phase");

    /// <summary>
    /// Falhas no carregamento do catálogo (DB ou Redis indisponível). Engine mantém
    /// última versão válida em memória — counter sinaliza degradação operacional.
    /// </summary>
    public static readonly Counter<long> BlocklistLoadErrors =
        _meter.CreateCounter<long>("blocklist.load_errors",
            description: "Falhas no carregamento do catálogo de blocklist. Engine reusa última versão válida.");

    /// <summary>
    /// Cache hits do BlocklistEngine. Tag: layer (l1|l2). Permite dashboard de eficiência
    /// — hit ratio baixo em L1 pode indicar TTL agressivo demais ou churn de patterns.
    /// </summary>
    public static readonly Counter<long> BlocklistCacheHits =
        _meter.CreateCounter<long>("blocklist.cache.hits",
            description: "Cache hits do BlocklistEngine. Tag: layer (l1|l2)");

    /// <summary>Invocações de tool por fingerprint (tag: tool, fingerprint_prefix).</summary>
    public static readonly Counter<long> ToolInvocationsByFingerprint =
        _meter.CreateCounter<long>("tool.invocations.by_fingerprint",
            description: "Invocações de tool contadas por fingerprint. Tags: tool, fingerprint");

    /// <summary>Vezes que o circuit breaker abriu para um provider. Tag: provider.</summary>
    public static readonly Counter<long> CircuitBreakerOpened =
        _meter.CreateCounter<long>("llm.circuit_breaker.opened",
            description: "Vezes que o circuit breaker abriu para um provider LLM. Tag: provider");

    /// <summary>Requests rejeitados por circuit open (fail-fast). Tag: provider.</summary>
    public static readonly Counter<long> CircuitBreakerRejected =
        _meter.CreateCounter<long>("llm.circuit_breaker.rejected",
            description: "Requests rejeitados por circuit open (fail-fast). Tag: provider");

    /// <summary>Requests roteados para fallback provider. Tag: primary, fallback.</summary>
    public static readonly Counter<long> CircuitBreakerFallbacks =
        _meter.CreateCounter<long>("llm.circuit_breaker.fallbacks",
            description: "Requests roteados para fallback provider. Tags: primary, fallback");

    /// <summary>Exceções não tratadas capturadas pelo GlobalExceptionMiddleware. Tags: path, method.</summary>
    public static readonly Counter<long> UnhandledExceptions =
        _meter.CreateCounter<long>("http.unhandled_exceptions",
            description: "Exceções não tratadas capturadas pelo GlobalExceptionMiddleware. Tags: path, method");

    /// <summary>
    /// Subscribers SSE ativos (conns PG "sse" em LISTEN). Proxy direto pro uso do pool:
    /// se cruzar SseMaxPoolSize, próximas subscrições vão esperar/timeout.
    /// </summary>
    public static readonly UpDownCounter<int> EventBusActiveSubscriptions =
        _meter.CreateUpDownCounter<int>("eventbus.active_subscriptions",
            description: "Subscribers SSE ativos (conn PG dedicada em LISTEN)");

    /// <summary>
    /// Timeouts da task background WaitAsync durante dispose (limite: 2s). Valor > 0 indica
    /// que a conn pode ter voltado ao pool em estado inconsistente — investigar pressão sobre pool SSE.
    /// </summary>
    public static readonly Counter<long> EventBusBackgroundTaskTimeouts =
        _meter.CreateCounter<long>("eventbus.background_task.timeouts",
            description: "Task background do LISTEN não concluiu no timeout de dispose (default 2s)");

    /// <summary>
    /// Falhas no setup do subscriber, antes de produzir qualquer evento. Tag: phase (open|listen|replay).
    /// </summary>
    public static readonly Counter<long> EventBusSubscribeSetupErrors =
        _meter.CreateCounter<long>("eventbus.subscribe.setup_errors",
            description: "Erros durante o setup do subscriber (open/listen/replay). Tag: phase");

    /// <summary>
    /// Latência da resolução de persona, com tag <c>outcome</c> =
    /// cache_hit_l1 | cache_hit_l2 | api_hit | fallback.
    /// </summary>
    public static readonly Histogram<double> PersonaResolutionDurationMs =
        _meter.CreateHistogram<double>("persona.resolution.duration_ms", unit: "ms",
            description: "Latência da resolução de persona. Tag: outcome=cache_hit_l1|cache_hit_l2|api_hit|fallback");

    /// <summary>
    /// Conta falhas da API externa que caíram em fallback Anonymous.
    /// Spike indica indisponibilidade do provider de persona.
    /// </summary>
    public static readonly Counter<long> PersonaResolutionFailures =
        _meter.CreateCounter<long>("persona.resolution.failures",
            description: "Falhas na resolução de persona que caíram para fallback Anonymous.");

    /// <summary>
    /// Tamanho em caracteres do system block de persona composto — detecta inchaço
    /// acidental (ex: tone_policy crescendo). Proxy para tokens (~4 chars/token).
    /// </summary>
    public static readonly Histogram<double> PersonaPromptComposeChars =
        _meter.CreateHistogram<double>("persona.prompt.compose.chars",
            description: "Tamanho (chars) do bloco de persona inserido no system message.");

    /// <summary>
    /// Incrementa cada vez que o composer atribui uma variant (A/B) a um
    /// usuário sob experiment ativo. Tag <c>variant</c>. Permite ver a taxa
    /// de assignments em tempo real sem esperar o batch writer do
    /// <c>llm_token_usage</c>.
    /// </summary>
    public static readonly Counter<long> PersonaExperimentAssignments =
        _meter.CreateCounter<long>("persona.experiment.assignments",
            description: "Assignments de variant em experiments A/B de persona. Tags: experiment_id, variant.");

    /// <summary>
    /// Incrementa quando o composer encontra um experiment ativo apontando
    /// pra <see cref="EfsAiHub.Core.Abstractions.Identity.Persona.PersonaPromptTemplateVersion.VersionId"/>
    /// que não existe mais (version deletada direto no DB). Composer degrada
    /// pro ActiveVersionId do template pai — sinalizar pra alertar admin que
    /// o experiment virou zumbi.
    /// </summary>
    public static readonly Counter<long> PersonaExperimentOrphanedVariants =
        _meter.CreateCounter<long>("persona.experiment.orphaned_variants",
            description: "Experiments cuja variant aponta pra VersionId órfã. Tags: experiment_id.");

    // ADR 0015 — Evaluation metrics. Tags low-cardinality apenas
    // (agent_definition_name, trigger_source, evaluator_kind). agent_id UUID
    // vai pra exemplars/structured logs, nunca como tag.

    /// <summary>Duração total de uma eval run. Tags: agent_definition_name, status, trigger_source.</summary>
    public static readonly Histogram<double> EvaluationsRunDurationMs =
        _meter.CreateHistogram<double>("evaluations.run.duration_ms", unit: "ms",
            description: "Duração de eval runs. Tags: agent_definition_name, status, trigger_source.");

    /// <summary>Custo em USD por run. Tags: agent_definition_name, evaluator_kind.</summary>
    public static readonly Histogram<double> EvaluationsRunCostUsd =
        _meter.CreateHistogram<double>("evaluations.run.cost_usd",
            description: "Custo (USD) por eval run. Tags: agent_definition_name, evaluator_kind.");

    /// <summary>Score 0..1 por case+evaluator. Tags: agent_definition_name, evaluator_name.</summary>
    public static readonly Histogram<double> EvaluationsCaseScore =
        _meter.CreateHistogram<double>("evaluations.case.score",
            description: "Score (0..1) por case+evaluator. Tags: agent_definition_name, evaluator_name.");

    /// <summary>Runs disparadas. Tag: trigger_source (Manual|AgentVersionPublished|ApiClient).</summary>
    public static readonly Counter<long> EvaluationsRunsTriggered =
        _meter.CreateCounter<long>("evaluations.runs.triggered",
            description: "Eval runs enfileiradas. Tag: trigger_source.");

    public static readonly Counter<long> EvaluationsRunsStarted =
        _meter.CreateCounter<long>("evaluations.runs.started",
            description: "Eval runs que transitaram Pending→Running. Tag: trigger_source.");

    public static readonly Counter<long> EvaluationsRunsCompleted =
        _meter.CreateCounter<long>("evaluations.runs.completed",
            description: "Eval runs Completed. Tag: trigger_source.");

    public static readonly Counter<long> EvaluationsRunsFailed =
        _meter.CreateCounter<long>("evaluations.runs.failed",
            description: "Eval runs Failed. Tags: trigger_source, error_category.");

    public static readonly Counter<long> EvaluationsRunsCancelled =
        _meter.CreateCounter<long>("evaluations.runs.cancelled",
            description: "Eval runs Cancelled pelo operador via API.");

    /// <summary>
    /// Pass rate caiu vs run anterior no mesmo (agent_def, testset_version,
    /// evaluator_config_version). Threshold em ADR 0015. Tag: agent_definition_name.
    /// </summary>
    public static readonly Counter<long> EvaluationsRegressionDetected =
        _meter.CreateCounter<long>("evaluations.regression.detected",
            description: "Regressão detectada vs baseline run. Tag: agent_definition_name.");

    /// <summary>
    /// Handler de autotrigger crashou após publish do AgentVersion commitar
    /// (enqueue de eval falhou). Não bloqueia publish; alerta operacional.
    /// </summary>
    public static readonly Counter<long> EvaluationsAutotriggerFailed =
        _meter.CreateCounter<long>("evaluations.autotrigger.failed",
            description: "Handler de autotrigger crashou ao tentar enqueue. Publish do AgentVersion não foi afetado.");

    /// <summary>
    /// Autotrigger no-op: AgentDefinition sem regression_test_set_id. Não é
    /// falha — sinaliza nudge pro operador configurar baseline.
    /// </summary>
    public static readonly Counter<long> EvaluationsAutotriggerSkippedNoConfig =
        _meter.CreateCounter<long>("evaluations.autotrigger.skipped_no_config",
            description: "Autotrigger no-op: AgentDefinition sem regression_test_set_id configurado.");

    /// <summary>Heartbeat age (segundos) do runner mais antigo Running. Gauge observável.</summary>
    private static long _evaluationsHeartbeatAgeSeconds;
    public static void SetEvaluationsHeartbeatAgeSeconds(long value) =>
        Interlocked.Exchange(ref _evaluationsHeartbeatAgeSeconds, value);
    public static readonly ObservableGauge<long> EvaluationsHeartbeatAgeSeconds =
        _meter.CreateObservableGauge<long>("evaluations.runner.heartbeat_age_seconds",
            () => Interlocked.Read(ref _evaluationsHeartbeatAgeSeconds),
            description: "Idade em segundos do heartbeat mais antigo (run Running mais defasada).");

    /// <summary>Profundidade da fila Pending. Gauge observável.</summary>
    private static long _evaluationsQueueDepth;
    public static void SetEvaluationsQueueDepth(long value) =>
        Interlocked.Exchange(ref _evaluationsQueueDepth, value);
    public static readonly ObservableGauge<long> EvaluationsQueueDepth =
        _meter.CreateObservableGauge<long>("evaluations.run.queue_depth",
            () => Interlocked.Read(ref _evaluationsQueueDepth),
            description: "Eval runs em status Pending aguardando pickup do runner.");

    /// <summary>Reaper marcou run Running como Failed por timeout de heartbeat.</summary>
    public static readonly Counter<long> EvaluationsRunsReaped =
        _meter.CreateCounter<long>("evaluations.runs.reaped",
            description: "Runs Running sem heartbeat há > timeout marcadas Failed pelo reaper.");

    /// <summary>
    /// Duração total do preload de segredos no boot (varre Bootstrap + projects +
    /// agents do DB, resolve cada identificador AWS único). Picos sugerem AWS lento
    /// ou crescimento do dataset.
    /// </summary>
    public static readonly Histogram<double> SecretsPreloadDurationMs =
        _meter.CreateHistogram<double>("secrets.preload_duration_ms", unit: "ms",
            description: "Duração do preload de segredos no boot.");

    /// <summary>
    /// Falhas individuais durante o preload (ResourceNotFound, AccessDenied etc.).
    /// Boot não é abortado por falha individual — caller que referenciar receberá
    /// null em runtime e usará sua mensagem própria. Alerta quando &gt; 0.
    /// </summary>
    public static readonly Counter<long> SecretsPreloadFailures =
        _meter.CreateCounter<long>("secrets.preload_failures_total",
            description: "Identificadores AWS que falharam no preload do boot. Caller recebe null em runtime.");

    public static readonly Counter<long> SecretsLiteralDetected =
        _meter.CreateCounter<long>("secrets.literal_detected_total",
            description: "Valor literal (não-referência) chegou ao runtime store. Indica que algum caminho ainda passa credencial em claro.");

    /// <summary>
    /// Invocações de Generic Tools (HTTP genéricas) executadas por agentes. Tags:
    /// tool_id, project, success (true|false), status_class (2xx|4xx|5xx|error).
    /// status_class=error em timeout/network/parse — sucesso=false sempre que o
    /// envelope retornado tem Success=false.
    /// </summary>
    public static readonly Counter<long> GenericToolInvocations =
        _meter.CreateCounter<long>("generic_tools.invocations_total",
            description: "Invocações de Generic Tool. Tags: tool_id, project, success, status_class.");

    /// <summary>
    /// Latência da chamada HTTP de Generic Tool (build URL → response parsed).
    /// Tags: tool_id, success.
    /// </summary>
    public static readonly Histogram<double> GenericToolDurationMs =
        _meter.CreateHistogram<double>("generic_tools.duration_ms",
            unit: "ms",
            description: "Duração de execução de Generic Tool. Tags: tool_id, success.");

    /// <summary>
    /// Eventos do middleware de memória operacional. Tag <c>event</c> com valores:
    /// <c>hit</c> (memória existia e foi injetada), <c>miss</c> (primeiro turno do escopo),
    /// <c>write</c> (Upsert OK), <c>strip</c> (campo removido do output devolvido),
    /// <c>parse_failure</c> (output não é JSON válido), <c>size_exceeded</c> (payload acima do MaxBytes),
    /// <c>streaming_buffered</c> (stream foi acumulado pra strip no fim),
    /// <c>concurrency_conflict</c> (Version divergente — write rejeitada),
    /// <c>no_op_tool_call</c> (LLM emitiu tool call em vez de texto),
    /// <c>no_context</c> (sem ProjectId/ScopeId — middleware no-op).
    /// Tag adicional <c>agent_id</c>.
    /// </summary>
    public static readonly Counter<long> OperationalMemoryEvents =
        _meter.CreateCounter<long>("operational_memory.events_total",
            description: "Eventos do middleware de memória operacional. Tags: event, agent_id.");

    /// <summary>
    /// Eventos do middleware de guardrails de segurança. Tag <c>event</c> com valores:
    /// <c>policy_injected</c> (system message com a safety policy adicionada à lista enviada ao LLM).
    /// Tag adicional <c>agent_id</c>.
    /// </summary>
    public static readonly Counter<long> SecurityEvents =
        _meter.CreateCounter<long>("security.events_total",
            description: "Eventos do middleware de guardrails de segurança. Tags: event, agent_id.");

    // ── Router observability ────────────────────────────────────────────────
    //
    // Camada 1 do plano de ambiguidade: telemetria por decisão do Router pra
    // dashboards (distribuição de classes guard-rail) e debug ad-hoc. Emitidas
    // por RouterDecisionTelemetryChatClient após cada turno do Router.

    /// <summary>
    /// Total de decisões emitidas pelo Router (uma por turno, valor da intent
    /// escolhida no enum canônico). Tags: <c>agent_id</c>, <c>intent</c>,
    /// <c>project_id</c>. Dashboards quebram por intent pra ver distribuição
    /// (ex: % out_of_scope, % needs_clarification, % por intent de negócio).
    /// </summary>
    public static readonly Counter<long> RouterDecisions =
        _meter.CreateCounter<long>("router.decisions_total",
            description: "Decisões do Router (1 por turno). Tags: agent_id, intent, project_id.");

    /// <summary>
    /// Histograma da confidence emitida pelo Router junto com a intent
    /// escolhida (range [0, 1]). Tags: <c>agent_id</c>, <c>intent</c>.
    /// Útil pra detectar regressões de calibração (ex: confidence média
    /// caindo em intents de negócio indica que o catálogo precisa de mais
    /// exemplos ou descrição melhor).
    /// </summary>
    public static readonly Histogram<double> RouterConfidence =
        _meter.CreateHistogram<double>("router.confidence",
            description: "Confidence emitida pelo Router por turno. Tags: agent_id, intent.");

    /// <summary>
    /// Eventos discretos relacionados à ambiguidade no Router. Tag
    /// <c>signal</c> com valores:
    /// <c>dominant</c> (intent de negócio escolhida — caso feliz, gap satisfez a regra de dominância),
    /// <c>needs_clarification</c> (Router pediu desambiguação válida),
    /// <c>out_of_scope</c> (Router classificou fora do domínio),
    /// <c>invalid_clarification</c> (LLM emitiu needs_clarification sem candidate_intents válido — rewrite via schema violation),
    /// <c>loop_guard_triggered</c> (LLM emitiu needs_clarification num turno onde o estado anterior já era needs_clarification — rewrite server-side força out_of_scope; distinto de invalid_clarification pra que dashboards separem "LLM bugou schema" vs "ambiguidade não resolvida em N tentativas"),
    /// <c>parse_failure</c> (output não foi JSON válido — telemetry no-op).
    /// Tag adicional <c>agent_id</c>. North-star (time-to-resolution) é medido
    /// fora; este counter é o guard-rail de distribuição.
    /// </summary>
    public static readonly Counter<long> RouterAmbiguitySignals =
        _meter.CreateCounter<long>("router.ambiguity_signals_total",
            description: "Sinais de ambiguidade do Router. Tags: signal, agent_id.");

    // ── Standalone pools ────────────────────────────────────────────────────

    /// <summary>Total de jobs standalone enfileirados via POST /responses ou /ingestions. Tags: project_id, source.</summary>
    public static readonly Counter<long> StandaloneJobsEnqueued =
        _meter.CreateCounter<long>("standalone.jobs_enqueued_total",
            description: "Jobs standalone enfileirados. Tags: project_id, source (responses|ingestions).");

    /// <summary>Total de jobs standalone que atingiram estado terminal. Tags: workflow_id, status (Completed|Failed|Cancelled).</summary>
    public static readonly Counter<long> StandaloneJobsCompleted =
        _meter.CreateCounter<long>("standalone.jobs_completed_total",
            description: "Jobs standalone terminais. Tags: workflow_id, status.");

    /// <summary>Tempo decorrido entre <c>CreatedAt</c> e <c>StartedAt</c> (Queued → Running). Saturação aparece aqui antes de virar latência total. Tags: workflow_id.</summary>
    public static readonly Histogram<double> StandaloneJobQueueSeconds =
        _meter.CreateHistogram<double>("standalone.job_queue_seconds", unit: "s",
            description: "Tempo de espera na fila standalone (Queued → Running). Tags: workflow_id.");

    /// <summary>Tempo total <c>CreatedAt → CompletedAt</c> em terminal. Tags: workflow_id, status.</summary>
    public static readonly Histogram<double> StandaloneJobTotalSeconds =
        _meter.CreateHistogram<double>("standalone.job_total_seconds", unit: "s",
            description: "Latência ponta-a-ponta de jobs standalone. Tags: workflow_id, status.");

    /// <summary>
    /// Rejeições no momento da admissão ou no dispatcher. Tags: reason
    /// (per_workflow_cap | global_safety | feature_disabled | duplicate_idempotency).
    /// </summary>
    public static readonly Counter<long> StandaloneAdmissionRejected =
        _meter.CreateCounter<long>("standalone.admission_rejected_total",
            description: "Jobs standalone rejeitados antes de rodar. Tags: reason.");

    /// <summary>Contador incrementado pelo StuckLeaseReaper quando devolve jobs pra Queued ou promove pra Failed por MaxAttempts.</summary>
    public static readonly Counter<long> StandaloneStuckLeasesRecovered =
        _meter.CreateCounter<long>("standalone.stuck_leases_recovered_total",
            description: "Jobs com lease expirado resgatados pelo reaper. Tags: outcome (requeued|failed_max_attempts).");

    /// <summary>
    /// Incrementado cada vez que um job de ingestão volta pra fila por falta de
    /// capacidade do Document Intelligence (gate cheio). É espera, NÃO erro — taxa
    /// alta e sustentada deste contador é o sinal de que falta capacidade
    /// (escalar <c>MaxConcurrentExtractions</c> ou o Azure DI). Tags: error_code.
    /// </summary>
    public static readonly Counter<long> IngestionCapacityWaits =
        _meter.CreateCounter<long>("ingestion.capacity_waits_total",
            description: "Re-enfileiramentos de ingestão por falta de capacidade do Document Intelligence (espera, não erro). Tags: error_code.");

    /// <summary>
    /// De onde o texto extraído foi recuperado ao montar o input do workflow. O
    /// texto NÃO vive mais no JSONB do job: no caminho feliz vem em memória
    /// (<c>memory</c>); numa retomada pós-extração é hidratado do S3 pelo ponteiro
    /// (<c>s3</c>) ou, em miss, re-baixado/re-extraído via cache do DI
    /// (<c>reextract</c>). <c>capacity_wait</c> = sem vaga no gate pra re-extrair
    /// agora (espera, não erro); <c>failed</c> = não foi possível recuperar (S3 +
    /// cache + origem indisponíveis). Taxa alta de <c>reextract</c>/<c>failed</c>
    /// sinaliza S3/Redis degradados. Tags: source.
    /// </summary>
    public static readonly Counter<long> IngestionContentHydrations =
        _meter.CreateCounter<long>("ingestion.content_hydrations_total",
            description: "Origem do texto extraído ao montar o input do workflow. Tags: source (memory|s3|reextract|capacity_wait|failed).");

    // ── Webhook deliveries ──────────────────────────────────────────────────

    /// <summary>
    /// Tentativas de entrega de webhook. Tags: outcome (delivered|failed),
    /// project_id. Sem retry: cada delivery emite UM evento.
    /// </summary>
    public static readonly Counter<long> WebhookDeliveryAttempts =
        _meter.CreateCounter<long>("webhook.delivery_attempts_total",
            description: "Tentativas de entrega de webhook (1 por delivery em v1). Tags: outcome, project_id.");

    /// <summary>Duração do POST de delivery (incluindo timeout). Tags: outcome.</summary>
    public static readonly Histogram<double> WebhookDeliveryDurationSeconds =
        _meter.CreateHistogram<double>("webhook.delivery_duration_seconds", unit: "s",
            description: "Duração do POST de entrega de webhook. Tags: outcome.");
}
