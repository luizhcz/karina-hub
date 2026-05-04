using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Executa uma chamada HTTP única configurada por um <see cref="GenericTool"/>.
/// Sem retry, sem circuit breaker, sem Polly. Em qualquer falha (timeout,
/// rede, status≥400, parse) retorna <see cref="ToolExecutionResult"/> com
/// <c>Success=false</c> — exceções nunca propagam pra cima do agente.
/// </summary>
public interface IGenericToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct = default);
}
