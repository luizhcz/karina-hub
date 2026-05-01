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

    public static readonly Counter<long> ToolAccountRejections =
        _meter.CreateCounter<long>("tool.account.rejections",
            description: "Tool calls rejeitados por divergência de conta (ex: SendOrder com boleta fora do cliente)");

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
    /// Contador de resoluções de secret cross-project (caller != owner do agent).
    /// Tags: caller, owner. Mostra que a separação de credentials por owner está funcionando.
    /// </summary>
    public static readonly Counter<long> SecretCrossProjectResolutions =
        _meter.CreateCounter<long>("secrets.cross_project_resolutions_total",
            description: "Resoluções de AWS Secret no contexto do agent owner (cross-project). Tags: caller, owner.");

    public static readonly Counter<long> StaleExecutionCompletionSkipped =
        _meter.CreateCounter<long>("chat.stale_completion.skipped",
            description: "Completions ignoradas por corresponderem a execução não mais ativa na conversa");

    public static readonly Counter<long> RobotMessagesPersisted =
        _meter.CreateCounter<long>("chat.robot_messages.persisted",
            description: "Mensagens com actor=robot registradas via short-circuit (sem disparar workflow). Ver ADR 0014.");

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

    public static readonly Counter<long> SecretsResolutionsTotal =
        _meter.CreateCounter<long>("secrets.resolutions_total",
            description: "Resoluções de secret. Tags: scope (global|project|agent|foundry), cache_layer (L1|L2|aws), result (hit|miss|error).");

    public static readonly Histogram<double> SecretsResolutionLatencyMs =
        _meter.CreateHistogram<double>("secrets.resolution_latency_ms", unit: "ms",
            description: "Latência de resolução de secret. Tags: cache_layer (L1|L2|aws).");

    public static readonly Counter<long> SecretsLiteralDetected =
        _meter.CreateCounter<long>("secrets.literal_detected_total",
            description: "Valor literal (não-referência) chegou ao resolver. Indica que algum caminho ainda passa credencial em claro.");
}
