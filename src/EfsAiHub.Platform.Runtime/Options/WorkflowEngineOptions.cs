namespace EfsAiHub.Platform.Runtime.Configuration;

public class WorkflowEngineOptions
{
    public const string SectionName = "WorkflowEngine";

    public int MaxConcurrentExecutions { get; init; } = 10;

    /// <summary>Capacidade máxima global de execuções Chat Path simultâneas (back-pressure). Default: 200.</summary>
    public int ChatMaxConcurrentExecutions { get; init; } = 200;
    public int DefaultTimeoutSeconds { get; init; } = 300;

    /// <summary>"InMemory" (default, não sobrevive a restarts) | "Postgres" (durável, recomendado para produção) | "Blob"</summary>
    public string CheckpointMode { get; init; } = "InMemory";

    public string? BlobCheckpointConnectionString { get; init; }
    public string? BlobCheckpointContainerName { get; init; }
    /// <summary>Limite de resumes concorrentes no HitlRecoveryService após restart. Default: 4.</summary>
    public int HitlRecoveryConcurrency { get; init; } = 4;

    /// <summary>Tamanho do lote por página quando HitlRecoveryService itera execuções Paused. Default: 100.</summary>
    public int HitlRecoveryBatchSize { get; init; } = 100;

    /// <summary>
    /// Intervalo em segundos entre ciclos periódicos do HitlRecoveryService.
    /// O primeiro ciclo roda imediatamente no startup. Ciclos subsequentes varrem
    /// execuções Paused que possam ter ficado órfãs (ex: NOTIFY perdido).
    /// 0 = desabilita polling periódico (apenas startup). Default: 30.
    /// </summary>
    public int HitlRecoveryIntervalSeconds { get; init; } = 30;

    /// <summary>Dias de retenção para workflow_event_audit (drop de partições antigas). Default: 30.</summary>
    public int AuditRetentionDays { get; init; } = 30;

    /// <summary>Dias de retenção para tool_invocations + llm_token_usage. Default: 14.</summary>
    public int ToolInvocationRetentionDays { get; init; } = 14;

    /// <summary>Dias de retenção para workflow_checkpoints órfãos (DELETE por UpdatedAt). Default: 14.</summary>
    public int CheckpointRetentionDays { get; init; } = 14;

    /// <summary>
    /// Segundos de grace period antes de cancelar automaticamente uma execução Interactive
    /// (Chat/API) após o SSE desconectar sem RUN_FINISHED. 0 = desabilita o auto-cancel.
    /// Execuções com HITL pendente nunca são canceladas pelo disconnect — o timeout do HITL governa.
    /// Default: 120 segundos.
    /// </summary>
    public int DisconnectGracePeriodSeconds { get; init; } = 120;

    /// <summary>
    /// Fase 6 — quando <c>true</c> (default), o <c>ChatOptionsBuilder</c> cai para a
    /// última versão de uma tool se o <c>FingerprintHash</c> snapshoteado não existir
    /// mais no <c>FunctionToolRegistry</c> (com warning). Quando <c>false</c>, a
    /// execução falha fast com <see cref="ToolFingerprintMismatchException"/>.
    /// Recomendado <c>false</c> em produção após a janela de migração.
    /// </summary>
    public bool AllowToolFingerprintMismatch { get; init; } = true;
}

/// <summary>
/// Fase 6 — lançada quando um agente referencia uma tool cujo fingerprint snapshoteado
/// não corresponde a nenhuma versão registrada no <c>FunctionToolRegistry</c>,
/// com <c>AllowToolFingerprintMismatch=false</c>.
/// </summary>
public sealed class ToolFingerprintMismatchException : InvalidOperationException
{
    public string AgentId { get; }
    public string ToolName { get; }
    public string ExpectedFingerprint { get; }

    public ToolFingerprintMismatchException(string agentId, string toolName, string expectedFingerprint)
        : base($"Agent '{agentId}': tool '{toolName}' fingerprint '{expectedFingerprint[..Math.Min(12, expectedFingerprint.Length)]}…' não está mais disponível no registry.")
    {
        AgentId = agentId;
        ToolName = toolName;
        ExpectedFingerprint = expectedFingerprint;
    }
}
