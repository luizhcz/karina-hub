using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Core.Orchestration.Executors;

/// <summary>
/// Nó de entrada automático inserido em todos os workflows Graph.
///
/// Problema que resolve: InProcessExecution.RunStreamingAsync entrega o input inicial como
/// List&lt;ChatMessage&gt;, mas DelegateExecutor&lt;string, string&gt; e AIAgent só aceitam string
/// como tipo de entrada. Sem este executor, o workflow completa imediatamente sem executar nada.
///
/// Solução: ChatTriggerExecutor é inserido como start node real do WorkflowBuilder.
/// Ele recebe a List&lt;ChatMessage&gt;, extrai o texto do usuário e passa como string para
/// o primeiro nó real definido no workflow.
/// </summary>
public sealed class ChatTriggerExecutor : Executor<List<ChatMessage>, string>
{
    public const string FixedId = "__chat_trigger__";

    // Logger estático mantido como NullLogger para preservar Core livre de providers
    // concretos (AddConsole vivia na Infra). A wiring via DI fica para Fase 6/7.
    private static readonly ILogger _log = NullLogger<ChatTriggerExecutor>.Instance;

    public ChatTriggerExecutor() : base(FixedId) { }

    public override ValueTask<string> HandleAsync(
        List<ChatMessage> input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        _log.LogInformation("[ChatTriggerExecutor] HandleAsync chamado com {Count} mensagens.", input.Count);

        var text = input
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text ?? string.Empty)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? input.Select(m => m.Text ?? string.Empty).FirstOrDefault()
            ?? string.Empty;

        _log.LogInformation("[ChatTriggerExecutor] Texto extraído: {Text}", text[..Math.Min(100, text.Length)]);
        return ValueTask.FromResult(text);
    }
}
