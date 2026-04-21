using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Nó bridge gerado automaticamente pelo engine quando uma edge tem InputSource="WorkflowInput".
/// Substitui o input recebido (output do nó anterior) pelo input original do workflow
/// armazenado em <see cref="DelegateExecutor.Current"/> via AsyncLocal.
///
/// Elimina a necessidade de nós ContextBridge manuais no workflow JSON.
/// </summary>
public sealed class InputSourceBridgeExecutor : Executor<string, string>
{
    public InputSourceBridgeExecutor(string targetNodeId)
        : base($"__bridge_{targetNodeId}__") { }

    public override ValueTask<string> HandleAsync(
        string input, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var originalInput = DelegateExecutor.Current.Value?.Input ?? input;
        return ValueTask.FromResult(originalInput);
    }
}
