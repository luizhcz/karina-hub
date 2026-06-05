namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Observer scoped por execução. Quebra o acoplamento direto
/// ExecutionFailureWriter → ConversationService: o writer publica eventos
/// terminais para todos os observers registrados (multi-bind via DI), cada
/// observer decide se reage com base nos identificadores.
///
/// Implementações devem ser idempotentes e tolerar IDs desconhecidos
/// (ex.: execução não-chat → ConversationService ignora).
/// </summary>
public interface IExecutionLifecycleObserver
{
    /// <summary>
    /// Persiste a mensagem assistant produzida por um step terminal de agente.
    /// Chamado pelo worker imediatamente após o output do agente ser materializado
    /// (handoff ou fim de execução). O <paramref name="messageId"/> é gerado pelo
    /// worker e já foi emitido nos eventos AG-UI (TEXT_MESSAGE_*, token) — o
    /// observer apenas usa o mesmo ID no save pra cliente conseguir referenciar
    /// a mensagem (ex.: feedback).
    /// </summary>
    Task OnStepCompletedAsync(
        string conversationId,
        string executionId,
        string agentId,
        string messageId,
        string output,
        CancellationToken ct = default);

    /// <summary>
    /// Sinaliza fim de execução (sem persistir mensagem — isso é responsabilidade
    /// de <see cref="OnStepCompletedAsync"/>). Permite ao observer fechar estado
    /// da conversa: zerar ActiveExecutionId, atualizar LastActiveAgentId/title, etc.
    /// </summary>
    Task OnExecutionCompletedAsync(
        string conversationId,
        string finalOutput,
        string executionId,
        string? lastActiveAgentId = null,
        CancellationToken ct = default);

    Task OnExecutionFailedAsync(
        string conversationId,
        string executionId,
        CancellationToken ct = default);

    Task OnRecoveryFailedAsync(
        string conversationId,
        string executionId,
        string reason,
        CancellationToken ct = default);
}
