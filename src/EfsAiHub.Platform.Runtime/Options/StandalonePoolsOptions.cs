namespace EfsAiHub.Platform.Runtime.Configuration;

/// <summary>
/// Opções dos pools standalone — workflows não-chat enfileirados em
/// <c>background_response_jobs</c> e consumidos pelo
/// <c>StandaloneJobDispatcherService</c>.
///
/// Quando <see cref="Enabled"/>=<c>false</c> (default), o dispatcher fica
/// dormente — o endpoint POST de enqueue, o consumer e o reaper não rodam.
/// Permite mergear a infra em prod sem ativar a feature, e ligar via
/// <c>StandalonePools__Enabled=true</c> quando confiança suficiente.
/// </summary>
public sealed class StandalonePoolsOptions
{
    public const string SectionName = "StandalonePools";

    /// <summary>
    /// Feature flag global. <c>false</c> mantém endpoints respondendo 503,
    /// HostedServices parados, sem efeito colateral. Default: <c>false</c>.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Teto cross-pod de jobs em <c>Running</c> simultaneamente (safety net
    /// global). Protege o pool Npgsql contra um workflow com
    /// <see cref="DefaultMaxConcurrentPerWorkflow"/> alto. Default: 120.
    /// </summary>
    public int GlobalConcurrency { get; init; } = 120;

    /// <summary>
    /// Cota por workflow quando o <c>WorkflowDefinition.Configuration</c> não
    /// declara um valor próprio. Aplicada via <c>COUNT(*) WHERE WorkflowId=X</c>
    /// no dispatcher antes do lease. Default: 12.
    /// </summary>
    public int DefaultMaxConcurrentPerWorkflow { get; init; } = 12;

    /// <summary>
    /// TTL do lease quando um job é leased. Heartbeat (<see cref="HeartbeatSeconds"/>)
    /// renova periodicamente. Lease vencido sem heartbeat → reaper devolve pra Queued.
    /// Default: 30s.
    /// </summary>
    public int LeaseTtlSeconds { get; init; } = 30;

    /// <summary>
    /// Intervalo do heartbeat — deve ser menor que <see cref="LeaseTtlSeconds"/>
    /// pra dar margem. Default: 10s.
    /// </summary>
    public int HeartbeatSeconds { get; init; } = 10;

    /// <summary>
    /// Intervalo do loop do dispatcher quando não há jobs Queued. Em produção,
    /// também escutamos <c>LISTEN/NOTIFY</c> em <c>efs_jobs_enqueued</c> pra
    /// reagir sem polling — mas pra v1 o polling é a base. Default: 5s.
    /// </summary>
    public int PollIdleSeconds { get; init; } = 5;

    /// <summary>
    /// Tamanho do lote por iteração do dispatcher. Cada worker do pool consome
    /// um job. Default: 25.
    /// </summary>
    public int BatchSize { get; init; } = 25;

    /// <summary>
    /// Máximo de tentativas por job antes de marcar <c>Status=Failed</c>
    /// permanente. Default: 3.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base do backoff exponencial entre retries do dispatcher quando o
    /// workflow falha — <c>nextAttempt = base * 2^attempt</c>. Default: 5
    /// (sequência 5s, 10s, 20s, 40s…).
    /// </summary>
    public int RetryBackoffBaseSeconds { get; init; } = 5;

    /// <summary>
    /// Backoff base (segundos) entre re-checagens quando um job está esperando
    /// capacidade de um gate externo (ex.: Document Intelligence cheio). É espera,
    /// não falha: o job volta pra <c>Queued</c> sem consumir <c>Attempt</c> e sem
    /// teto — aguarda na fila o tempo que for até abrir vaga. O valor real aplicado
    /// é <c>base + jitter(0..base)</c>, pra espalhar o thundering herd quando muitos
    /// jobs caem no mesmo gate cheio. Maior que <see cref="RetryBackoffBaseSeconds"/>
    /// de propósito: re-checar de 15 em 15s (e não de 5 em 5s) reduz o churn de
    /// lease/handler sob fila grande, ao custo de até ~2x base de latência extra pra
    /// pegar uma vaga recém-liberada — irrelevante numa espera de minutos/horas.
    /// Default: 15 (espera real de 15–30s por ciclo).
    /// </summary>
    public int CapacityWaitBackoffSeconds { get; init; } = 15;

    /// <summary>
    /// Intervalo do <c>StuckLeaseReaper</c> — varre leases expirados e devolve
    /// jobs pra Queued. Default: 30s (rápido pra que pod crash não trave job
    /// muito mais que o LeaseTtl).
    /// </summary>
    public int ReaperIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Backoff aplicado pelo reaper quando devolve um lease expirado pra Queued
    /// — <c>NextAttemptAt = now() + N segundos</c>. Distinto de
    /// <see cref="RetryBackoffBaseSeconds"/> (que é para falha do workflow);
    /// aqui o motivo é pod morto/heartbeat falho. Default: 5s.
    /// </summary>
    public int ReaperReclaimBackoffSeconds { get; init; } = 5;

    /// <summary>
    /// Limite de polls/minuto por projeto no <c>GET /api/aihub/responses/{jobId}</c>
    /// (sliding window Redis). Default: 60.
    /// </summary>
    public int PollingRateLimitPerMinute { get; init; } = 60;

    /// <summary>
    /// Workflow standalone que demora mais que isso sem terminal é considerado
    /// travado (mesmo com leases renovados). Reaper não atua aqui — só o
    /// <c>StuckExecutionRecoveryService</c> existente cuida da execução. Default: 30min.
    /// </summary>
    public int JobMaxLifetimeMinutes { get; init; } = 30;
}
