namespace EfsAiHub.Core.Orchestration.Coordination;

/// <summary>
/// Fix #A1 — barramento de coordenação cross-pod via Postgres LISTEN/NOTIFY.
/// Usado para propagar cancelamento e resolução HITL para instâncias remotas.
/// Implementação singleton. Em K=1 o publish continua sendo um no-op observável.
/// </summary>
public interface ICrossNodeBus
{
    /// <summary>Propaga um pedido de cancelamento para todos os pods (canal efs_exec_cancel).</summary>
    Task PublishCancelAsync(string executionId, CancellationToken ct = default);

    /// <summary>Propaga uma resolução HITL para todos os pods (canal efs_hitl_resolved).</summary>
    Task PublishHitlResolvedAsync(
        string interactionId,
        string resolution,
        bool approved,
        CancellationToken ct = default);
}
