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

    /// <summary>Fase 2 — custo incremental em USD por chamada LLM.</summary>
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

    public static readonly Counter<long> BudgetExceededKills =
        _meter.CreateCounter<long>("llm.budget.exceeded",
            description: "Execuções mortas por estourar MaxTokensPerExecution");

    public static readonly Counter<long> StaleExecutionCompletionSkipped =
        _meter.CreateCounter<long>("chat.stale_completion.skipped",
            description: "Completions ignoradas por corresponderem a execução não mais ativa na conversa");

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

    // ── P2-A — Métricas operacionais HITL ───────────────────────────────────────

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

    // ── Fase 8 — Hardening / Observabilidade consolidada ─────────────────────

    /// <summary>Fase 1 — latência de resolução de AgentVersion (cache+DB).</summary>
    public static readonly Histogram<double> AgentVersionResolveLatency =
        _meter.CreateHistogram<double>("agent.version.resolve_latency", unit: "ms",
            description: "Latência de resolução de AgentVersion (lookup do snapshot corrente)");

    /// <summary>Fase 4 — latência de retrieval RAG por knowledge source.</summary>
    public static readonly Histogram<double> RagRetrievalLatency =
        _meter.CreateHistogram<double>("rag.retrieval.latency", unit: "ms",
            description: "Latência de retrieval RAG. Tag: source_kind=pgvector|foundry|ai_search");

    /// <summary>Fase 4 — documentos retornados por retrieval RAG.</summary>
    public static readonly Histogram<double> RagDocsReturned =
        _meter.CreateHistogram<double>("rag.docs.returned",
            description: "Quantidade de documentos retornados por retrieval RAG");

    /// <summary>Fase 7 — sinais de escalação emitidos (tag: category, routed=true|false).</summary>
    public static readonly Counter<long> EscalationSignalsTotal =
        _meter.CreateCounter<long>("agent.escalation.signals",
            description: "Sinais AgentEscalationSignal emitidos por agentes. Tags: category, routed");

    /// <summary>Fase 6 — invocações de tool por fingerprint (tag: tool, fingerprint_prefix).</summary>
    public static readonly Counter<long> ToolInvocationsByFingerprint =
        _meter.CreateCounter<long>("tool.invocations.by_fingerprint",
            description: "Invocações de tool contadas por fingerprint (Fase 6). Tags: tool, fingerprint");

    // ── Item 9 — Circuit Breaker LLM ──────────────────────────────────────────

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

    // ── PgEventBus — observabilidade do LISTEN/NOTIFY SSE ────────────────────

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

    // ── Persona Resolution ──────────────────────────────────────────────────

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
}
