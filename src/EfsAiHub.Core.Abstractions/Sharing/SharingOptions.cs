namespace EfsAiHub.Core.Abstractions.Sharing;

/// <summary>
/// Phase 3 — Feature flags pra rollback graceful do épico multi-projeto. Bound via
/// <c>IOptionsMonitor&lt;SharingOptions&gt;</c> — alterações em runtime sem restart.
///
/// Mora em Core.Abstractions porque é consumido por Platform.Runtime (AgentFactory),
/// Application services e Host.Api (registro DI). Camada-compartilhada por design.
/// </summary>
public sealed class SharingOptions
{
    public const string SectionName = "Sharing";

    /// <summary>
    /// Master switch. Quando false: <c>Visibility="global"</c> é ignorado em listagens
    /// (volta a project-only). Toggle UI escondido. Audit log mantém histórico.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Quando false: <c>WorkflowValidator</c> rejeita refs cross-project; runtime rejeita
    /// resolução cross-project com <c>UnauthorizedAccessException</c>. Útil pra desligar
    /// sharing efetivo mantendo os campos no domain (rollback granular).
    /// </summary>
    public bool CrossProjectEnabled { get; init; } = true;

    /// <summary>
    /// Quando false: <c>AgentDefinition.AllowedProjectIds</c> é ignorado. Útil quando se
    /// quer relaxar whitelist temporariamente (ex: incidente de produção bloqueando workflows).
    /// </summary>
    public bool WhitelistEnabled { get; init; } = true;

    /// <summary>
    /// Quando false: skip do audit <c>cross_project_invoke</c> (mantém métrica + log,
    /// só não persiste em <c>admin_audit_log</c>). Default true. Use pra reduzir
    /// pressão na tabela em workloads alto até Phase 4 introduzir partitioning.
    /// </summary>
    public bool AuditCrossInvoke { get; init; } = true;

    /// <summary>
    /// Janela de throttle (em segundos) pra cross_project_invoke audit log.
    /// Default 60s.
    /// </summary>
    public int CrossInvokeAuditThrottleSeconds { get; init; } = 60;
}
