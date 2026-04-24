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
