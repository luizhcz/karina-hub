using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Subset de <see cref="IWorkflowService"/> necessário para quem apenas precisa
/// disparar e controlar execuções (sem acesso ao CRUD de definitions).
/// Introduzido para quebrar a dependência lógica
/// <c>ConversationService → IWorkflowService</c> e o ciclo indireto com
/// <c>ExecutionFailureWriter → ConversationService</c>.
/// </summary>
public interface IWorkflowDispatcher
{
    Task<string> TriggerAsync(
        string workflowId,
        string? inputPayload,
        Dictionary<string, string>? metadata = null,
        ExecutionSource source = ExecutionSource.Api,
        ExecutionMode mode = ExecutionMode.Production,
        CancellationToken ct = default);

    Task<WorkflowExecution?> GetExecutionAsync(string executionId, CancellationToken ct = default);
    Task CancelExecutionAsync(string executionId, CancellationToken ct = default);
}
