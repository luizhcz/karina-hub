namespace EfsAiHub.Core.Agents.Responses;

/// <summary>
/// Execução assíncrona de um agente ou workflow standalone com polling (GET) como
/// caminho primário de leitura do estado. Persistido em
/// <c>aihub.background_response_jobs</c>. <see cref="ResponseCallbackTarget"/> é
/// armazenado para entrega via webhook, mas o worker dedicado de delivery vive
/// fora deste agregado — esse arquivo expõe apenas o estado da execução.
/// </summary>
public enum BackgroundResponseStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class BackgroundResponseJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string? AgentVersionId { get; set; }
    public string? SessionId { get; set; }
    public string Input { get; set; } = "";
    public BackgroundResponseStatus Status { get; set; } = BackgroundResponseStatus.Queued;
    public string? Output { get; set; }
    public string? LastError { get; set; }
    public int Attempt { get; set; }
    public ResponseCallbackTarget? CallbackTarget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Workflow alvo da execução assíncrona. Hot path da cota — toda iteração
    /// do dispatcher conta jobs por <c>WorkflowId</c>. Index B-tree dedicado.
    /// </summary>
    public string? WorkflowId { get; set; }

    /// <summary>
    /// Sub-estado dentro de <see cref="Status"/>=Running, populado por handlers
    /// que orquestram múltiplas etapas internas. Null pros jobs que vão direto
    /// pro workflow.
    /// </summary>
    public string? Step { get; set; }

    /// <summary>PodId que segurou o lease via FOR UPDATE SKIP LOCKED.</summary>
    public string? LeasedBy { get; set; }

    /// <summary>Quando o lease expira — heartbeat renova a cada N segundos.</summary>
    public DateTime? LeaseUntil { get; set; }

    /// <summary>
    /// Quando o próximo retry pode ser executado (backoff exponencial após
    /// falha não-permanente). Dispatcher filtra <c>Status='Queued' AND
    /// (NextAttemptAt IS NULL OR NextAttemptAt &lt;= now())</c>.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Contexto da máquina de estados de ingestão, JSON livre:
    /// <c>{extractionId, contentHash, pageCount, downloadedBytes, downloadedAt}</c>.
    /// Hidratado/atualizado a cada step pelo IngestionPipelineHandler.
    /// </summary>
    public string? IngestionContext { get; set; }

    /// <summary>
    /// <c>workflow_executions.execution_id</c> disparado por este job.
    /// Preenchido após o dispatcher chamar IWorkflowDispatcher.TriggerAsync.
    /// </summary>
    public string? ExecutionId { get; set; }

    /// <summary>
    /// Última atualização da row. Compõe o ETag do GET (junto com Status):
    /// cliente bate If-None-Match e o backend devolve 304 sem serializar o body.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Projeto que originou o job. Multi-tenant: GET filtra por
    /// (TenantId, ProjectId) — non-admin de outro projeto recebe 404 mesmo
    /// conhecendo o JobId.
    /// </summary>
    public string ProjectId { get; set; } = "default";

    /// <summary>Tenant ao qual o job pertence. Mesma justificativa de <see cref="ProjectId"/>.</summary>
    public string TenantId { get; set; } = "default";
}

/// <summary>
/// Webhook de notificação. O <see cref="HmacSecret"/> é usado para assinar o payload
/// (header <c>X-EfsAiHub-Signature: sha256=HEX</c>). Headers adicionais são mesclados.
/// </summary>
public sealed record ResponseCallbackTarget(
    string Url,
    string? HmacSecret = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public interface IBackgroundResponseRepository
{
    Task<BackgroundResponseJob> InsertAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task UpdateAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<IReadOnlyList<BackgroundResponseJob>> ListPendingAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Lê N jobs <c>Status='Queued'</c> elegíveis (NextAttemptAt vencido e cota
    /// por workflow não atingida) e marca-os como <c>Status='Running'</c> com
    /// lease pra <paramref name="podId"/>, tudo numa transação
    /// <c>FOR UPDATE SKIP LOCKED</c> — N réplicas pegam jobs disjuntos sem lock
    /// distribuído. A cota é aplicada na própria query (subquery correlated)
    /// pra evitar leasing + retry-by-cota num loop.
    /// </summary>
    Task<IReadOnlyList<BackgroundResponseJob>> TryLeaseAsync(
        int batchSize,
        string podId,
        TimeSpan leaseTtl,
        int perWorkflowCap,
        CancellationToken ct = default);

    /// <summary>Conta jobs <c>Status='Running'</c> de um workflow específico — observability/diagnostics.</summary>
    Task<int> CountRunningByWorkflowAsync(string workflowId, CancellationToken ct = default);

    /// <summary>Estende <c>LeaseUntil</c> sem mudar Status — heartbeat enquanto job está em execução.</summary>
    Task<bool> RenewLeaseAsync(string jobId, string podId, TimeSpan extend, CancellationToken ct = default);

    /// <summary>
    /// Reseta jobs com <c>LeaseUntil &lt; now()</c> de volta pra Queued, ou
    /// promove a Failed quando <c>Attempt &gt;= maxAttempts</c>. Evita loop
    /// infinito de retry em job determinísticamente travado.
    /// </summary>
    Task<int> ReclaimExpiredLeasesAsync(TimeSpan reclaimBackoff, int maxAttempts, CancellationToken ct = default);

    /// <summary>
    /// Atualiza <c>Step</c> + <c>UpdatedAt</c> mantendo Status='Running' e o
    /// lease ativo. Ownership-aware: filtra <c>LeasedBy = podId</c> no WHERE
    /// pra que pod stale (lease já roubado pelo reaper) vire no-op silencioso
    /// em vez de sobrescrever estado do dono atual.
    /// </summary>
    Task<bool> UpdateStepAsync(string jobId, string podId, string? step, CancellationToken ct = default);

    /// <summary>
    /// Atualiza <c>IngestionContext</c> (JSONB) + <c>UpdatedAt</c> sem mudar
    /// Status. Ownership-aware (mesma justificativa de <see cref="UpdateStepAsync"/>).
    /// Usado pelo IngestionJobHandler entre etapas pra persistir estado
    /// intermediário (contentLength, extractionId, extractedContent…) que
    /// sobrevive crash do pod e permite retomada do step.
    /// </summary>
    Task<bool> UpdateIngestionContextAsync(string jobId, string podId, string? ingestionContextJson, CancellationToken ct = default);

    /// <summary>
    /// Marca job como Completed e grava output. WHERE inclui ownership check
    /// (<paramref name="podId"/>) — retorna <c>false</c> quando lease já foi
    /// roubado, caller deve abortar sem reescrever estado.
    /// </summary>
    Task<bool> CompleteAsync(string jobId, string podId, string? output, CancellationToken ct = default);

    /// <summary>
    /// Marca falha com retry agendado (NextAttemptAt) ou permanente
    /// (NextAttemptAt=null + Status=Failed). WHERE inclui ownership check —
    /// retorna <c>false</c> quando lease já foi roubado.
    /// </summary>
    Task<bool> FailAsync(string jobId, string podId, string lastError, DateTime? nextAttemptAt, bool permanent, CancellationToken ct = default);

    /// <summary>Associa <c>ExecutionId</c> ao job após dispatch do workflow.</summary>
    Task SetExecutionIdAsync(string jobId, string executionId, CancellationToken ct = default);
}

public interface IBackgroundResponseService
{
    Task<BackgroundResponseJob> EnqueueAsync(BackgroundResponseJob job, CancellationToken ct = default);
    Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default);
    Task<bool> CancelAsync(string jobId, CancellationToken ct = default);
}
