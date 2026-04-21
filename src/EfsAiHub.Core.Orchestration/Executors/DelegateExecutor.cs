using Microsoft.Agents.AI.Workflows;
using ExecutionContext = EfsAiHub.Core.Agents.Execution.ExecutionContext;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Core.Orchestration.Executors;

/// <summary>
/// Executor de código puro (sem LLM) que encapsula um delegate assíncrono string→string.
/// Usado no modo Graph para etapas que não precisam de LLM (ex: HTTP calls, file I/O).
/// </summary>
public sealed class DelegateExecutor : Executor<string, string>
{
    private readonly Func<string, CancellationToken, Task<string>> _handler;

    /// <summary>
    /// Logger injetado por escopo via AsyncLocal — conectado ao pipeline OTel/ILoggerFactory do app.
    /// Ciclo de vida diferente do Current (por agente, não por execução) — mantido separado.
    /// </summary>
    public static readonly AsyncLocal<ILogger?> CurrentLogger = new();

    private static ILogger Log => CurrentLogger.Value ?? NullLogger.Instance;

    /// <summary>
    /// Contexto de execução corrente: ExecutionId, WorkflowId, Input, PromptVersions e NodeCallback.
    /// Definido pelo WorkflowRunnerService antes de iniciar o workflow; limpo no finally.
    /// </summary>
    public static readonly AsyncLocal<ExecutionContext?> Current = new();

    public DelegateExecutor(string id, Func<string, CancellationToken, Task<string>> handler)
        : base(id)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public override async ValueTask<string> HandleAsync(
        string input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // LogDebug: conteúdo de input/output pode conter PII (dados financeiros, CPF, mensagens).
        // Desabilitado em produção via LogLevel; visível apenas em desenvolvimento.
        Log.LogDebug("[DelegateExecutor:{Id}] HandleAsync chamado. Input: {Input}", Id, input[..Math.Min(200, input.Length)]);

        Current.Value?.NodeCallback?.Invoke(Id, false, input[..Math.Min(500, input.Length)]);

        var result = await _handler(input, cancellationToken);

        Current.Value?.NodeCallback?.Invoke(Id, true, result[..Math.Min(500, result.Length)]);

        Log.LogDebug("[DelegateExecutor:{Id}] Resultado: {Result}", Id, result[..Math.Min(200, result.Length)]);
        return result;
    }
}
