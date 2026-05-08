using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Executa uma chamada de teste de <see cref="GenericTool"/> — paralelo ao
/// <see cref="IGenericToolExecutor"/>, mas voltado pra debug humano: sem audit,
/// sem métricas, com timeout fixo curto e diagnóstico completo (status, headers,
/// body, parsed data) no envelope retornado.
/// </summary>
public interface IGenericToolTester
{
    Task<GenericToolTestResult> TestAsync(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct = default);
}
