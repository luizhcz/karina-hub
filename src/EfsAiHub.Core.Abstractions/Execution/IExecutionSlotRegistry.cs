namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Registry singleton de slots de concorrência + cancellation tokens
/// por execução. Quebra o acoplamento direto WorkflowService → ChatExecutionRegistry.
///
/// O nome "Slot" abstrai a noção de chat: qualquer caller (chat, batch, AG-UI)
/// pode adquirir um slot com seu próprio limite de concorrência.
///
/// Contagem de slots é distribuída via <see cref="IDistributedSlotCounter"/> (Redis),
/// enquanto CancellationTokens permanecem locais (in-process).
/// </summary>
public interface IExecutionSlotRegistry
{
    void Register(string executionId, CancellationTokenSource cts);
    bool TryCancel(string executionId);
    void Cleanup(string executionId);

    /// <summary>
    /// Tenta adquirir um slot global. Retorna false se o limite foi atingido
    /// (back-pressure). Cada slot adquirido DEVE ser liberado via <see cref="Cleanup"/>
    /// ou <see cref="ReleaseSlot"/>.
    /// Contagem distribuída via Redis — await obrigatório.
    /// </summary>
    Task<bool> TryAcquireSlotAsync();

    /// <summary>Libera um slot adquirido sem ter passado por Register/Cleanup (compensação).</summary>
    Task ReleaseSlotAsync();
}
