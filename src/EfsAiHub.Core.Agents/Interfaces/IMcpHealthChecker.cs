namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Phase 17b — Interface de verificação de saúde de servidores MCP.
/// Promovida para Core.Orchestration para quebrar dependência Platform.Runtime → Infra.Tools.
/// </summary>
public interface IMcpHealthChecker
{
    /// <summary>
    /// Retorna null quando o servidor está saudável, ou uma mensagem descritiva
    /// pronta para incluir em lista de erros de validação quando não está.
    /// </summary>
    Task<string?> CheckAsync(string serverUrl, string serverLabel, CancellationToken ct);
}
