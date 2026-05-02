namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Auto-pin lazy de refs de workflow sem AgentVersionId. Acionado pelo AgentFactory
/// no first execute pós <c>Sharing:MandatoryPin=true</c>. Idempotente: re-execuções
/// sem mudança não persistem nem auditam. Concorrência-safe: re-fetch defensivo
/// captura pins criados por instâncias paralelas.
/// </summary>
public interface IWorkflowAutoPinService
{
    /// <summary>
    /// Pra cada agent ref sem <c>AgentVersionId</c> em <paramref name="workflow"/>,
    /// resolve current Published version e popula no workflow row + no objeto in-memory.
    /// Persiste via UpsertAsync apenas quando há mudança efetiva. Audit
    /// <c>workflow.agent_version_auto_pinned</c> emitido apenas em mudança.
    /// </summary>
    Task AutoPinLegacyReferencesAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default);
}
