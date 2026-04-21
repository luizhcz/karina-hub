namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Contrato para gerenciamento de interações Human-in-the-Loop.
/// Abstrai o mecanismo de bloqueio/desbloqueio (TCS, checkpoint, etc.)
/// para permitir testes e futura migração para RequestInfoEvent/checkpoint-only.
/// </summary>
public interface IHumanInteractionService
{
    /// <summary>
    /// Registra uma requisição de interação e bloqueia até o humano responder
    /// (ou o CancellationToken ser cancelado).
    /// </summary>
    Task<string> RequestAsync(HumanInteractionRequest request, CancellationToken ct = default);

    /// <summary>Resolve uma interação pendente com a resposta do humano.</summary>
    bool Resolve(string interactionId, string resolution, bool approved = true, bool publishToCross = true);

    /// <summary>Retorna todas as interações pendentes em memória.</summary>
    IReadOnlyList<HumanInteractionRequest> GetPending();

    /// <summary>Busca uma interação por ID no cache em memória.</summary>
    HumanInteractionRequest? GetById(string interactionId);

    /// <summary>Retorna todas as interações de uma execução no cache em memória.</summary>
    IReadOnlyList<HumanInteractionRequest> GetByExecutionId(string executionId);

    /// <summary>
    /// Retorna a primeira interação com Status == Pending para o executionId, ou null.
    /// Usado pela camada de chat para detectar se o próximo turno deve resolver um HITL.
    /// </summary>
    HumanInteractionRequest? GetPendingForExecution(string executionId);

    /// <summary>
    /// Expira todas as interações Pending de uma execução cancelada ou falhada.
    /// </summary>
    Task ExpireForExecutionAsync(string executionId);

    // ── Métodos de recovery (startup / HitlRecoveryService) ─────────────────

    /// <summary>
    /// Carrega interações com Status=Pending do banco na inicialização do processo.
    /// </summary>
    Task LoadPendingFromDbAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-registra um mecanismo de espera para uma interação previamente carregada do banco.
    /// Retorna null se a interação não está no cache.
    /// </summary>
    TaskCompletionSource<string>? ReRegisterPending(string interactionId);

    /// <summary>
    /// Injeta uma interação (qualquer status) no cache em memória para recovery.
    /// </summary>
    void InjectForRecovery(HumanInteractionRequest request);
}
