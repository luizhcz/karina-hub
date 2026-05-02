namespace EfsAiHub.Core.Abstractions.Sharing;

/// <summary>
/// Feature flags pra rollback graceful do sharing cross-project. Bound via
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
    /// pressão na tabela em workloads alto.
    /// </summary>
    public bool AuditCrossInvoke { get; init; } = true;

    /// <summary>
    /// Janela de throttle (em segundos) pra cross_project_invoke audit log.
    /// Default 60s.
    /// </summary>
    public int CrossInvokeAuditThrottleSeconds { get; init; } = 60;

    /// <summary>
    /// Quando true: workflow save exige <c>WorkflowAgentReference.AgentVersionId</c>
    /// não-vazio em todos os refs (validado no <c>WorkflowValidator</c>); workflows
    /// legados sem pin são auto-pinados pelo <c>AgentFactory</c> no first execute
    /// pós-flag (ver <c>IWorkflowAutoPinService.AutoPinLegacyReferencesAsync</c>).
    /// Default false: rollout staged controlado, ativado por tenant após validação.
    /// Combinada com <see cref="MandatoryPinTenants"/> pra rollout incremental.
    /// </summary>
    public bool MandatoryPin { get; init; } = false;

    /// <summary>
    /// Whitelist de tenants em que <see cref="MandatoryPin"/> está enforcement-on.
    /// Semântica:
    /// <list type="bullet">
    ///   <item><c>null</c> ou lista vazia + MandatoryPin=true → enforcement GLOBAL (todos os tenants).</item>
    ///   <item>Lista com IDs + MandatoryPin=true → enforcement APENAS pra tenants listados.</item>
    ///   <item>MandatoryPin=false → ignorado (enforcement off em qualquer caso).</item>
    /// </list>
    /// Use pra rollout incremental: começar com 1 tenant piloto, expandir gradualmente.
    /// </summary>
    public IReadOnlyList<string>? MandatoryPinTenants { get; init; }

    /// <summary>
    /// Quando false: <c>AgentFactory</c> ignora <c>SchemaVersion=2</c> snapshots e cai
    /// sempre no path legado (resolve current via <c>IAgentDefinitionRepository</c>),
    /// como se todo pin fosse v1 lossy. Kill switch pra desligar lossless em caso de
    /// regressão. Default true: lossless ativo.
    /// </summary>
    public bool LosslessAgentVersion { get; init; } = true;

    /// <summary>
    /// Decide se <see cref="MandatoryPin"/> deve enforcer pra um tenant específico
    /// considerando o whitelist <see cref="MandatoryPinTenants"/>. Lógica:
    /// MandatoryPin OFF → false; MandatoryPin ON sem whitelist → true (global);
    /// MandatoryPin ON com whitelist → true só se tenantId está na lista.
    /// </summary>
    public bool IsMandatoryPinFor(string? tenantId)
    {
        if (!MandatoryPin) return false;
        if (MandatoryPinTenants is null || MandatoryPinTenants.Count == 0)
            return true;
        if (string.IsNullOrEmpty(tenantId))
            return false;
        return MandatoryPinTenants.Any(t =>
            string.Equals(t, tenantId, StringComparison.OrdinalIgnoreCase));
    }
}
