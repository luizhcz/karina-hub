namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Repositório durável para interações Human-in-the-Loop.
/// Garante que o estado HITL sobreviva a restarts do processo.
/// </summary>
public interface IHumanInteractionRepository
{
    Task CreateAsync(HumanInteractionRequest request, CancellationToken ct = default);
    Task UpdateAsync(HumanInteractionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HumanInteractionRequest>> GetPendingAsync(CancellationToken ct = default);
    Task<HumanInteractionRequest?> GetByIdAsync(string interactionId, CancellationToken ct = default);
    Task<IReadOnlyList<HumanInteractionRequest>> GetByExecutionIdAsync(string executionId, CancellationToken ct = default);
    /// <summary>
    /// Retorna a interação mais recente (qualquer status) para uma execução.
    /// Usado pelo HitlRecoveryService para detectar HITLs já resolvidos cujas execuções ainda estão Paused.
    /// </summary>
    Task<HumanInteractionRequest?> GetLatestByExecutionIdAsync(string executionId, CancellationToken ct = default);
    /// <summary>Marca como Expired todas as interações Pending de uma execução cancelada/falhada.</summary>
    Task ExpireByExecutionIdAsync(string executionId, CancellationToken ct = default);
    /// <summary>
    /// CAS (Compare-And-Swap) atômico a nível de banco: transiciona <c>Status</c> de
    /// <c>Pending</c> para <paramref name="newStatus"/> apenas se ainda estiver Pending.
    /// Retorna <c>true</c> se esta chamada foi quem resolveu; <c>false</c> se outro
    /// caller/pod já havia resolvido (ou o id não existe).
    /// Elimina race condition entre dois resolve concorrentes (ex: API + cross-pod NOTIFY).
    /// </summary>
    Task<bool> TryResolveAsync(
        string interactionId,
        EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus newStatus,
        string resolution,
        DateTime resolvedAt,
        CancellationToken ct = default);
    /// <summary>
    /// Expira em lote todos os HITLs Pending cujas execuções já estão em estado terminal
    /// (Failed, Cancelled, Completed). Chamado no startup para limpar registros órfãos
    /// acumulados antes do fix de expiração automática.
    /// </summary>
    Task ExpireOrphanedAsync(CancellationToken ct = default);
}
