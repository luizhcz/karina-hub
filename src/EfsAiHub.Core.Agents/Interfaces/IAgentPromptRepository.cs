
namespace EfsAiHub.Core.Agents;

/// <summary>
/// Repositório de versões de prompt por agente, armazenado em PostgreSQL.
///
/// Estrutura: agentId + versionId como chave composta
///   master      → nome da versão ativa (plain string, não JSON)
///   {versionId} → conteúdo do system prompt para aquela versão
/// </summary>
public interface IAgentPromptRepository
{
    /// <summary>
    /// Retorna o conteúdo do prompt ativo (versão apontada por master).
    /// Usa cache em memória com TTL. Retorna null se nenhuma versão estiver cadastrada.
    /// </summary>
    Task<string?> GetActivePromptAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Retorna o conteúdo e o versionId do prompt ativo.
    /// Permite propagar PromptVersionId para audit trail em LlmTokenUsage.
    /// </summary>
    Task<(string Content, string VersionId)?> GetActivePromptWithVersionAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Grava ou substitui o conteúdo de uma versão específica.
    /// Invalida o cache do agente.
    /// </summary>
    Task SaveVersionAsync(string agentId, string versionId, string content, CancellationToken ct = default);

    /// <summary>
    /// Move o ponteiro master para uma versão já existente.
    /// Lança KeyNotFoundException se a versão não existir.
    /// Invalida o cache do agente.
    /// </summary>
    Task SetMasterAsync(string agentId, string versionId, CancellationToken ct = default);

    /// <summary>
    /// Lista todas as versões cadastradas para o agente, indicando qual é a ativa.
    /// </summary>
    Task<IReadOnlyList<AgentPromptVersion>> ListVersionsAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Remove uma versão. Lança InvalidOperationException se for a versão ativa (master).
    /// Invalida o cache do agente.
    /// </summary>
    Task DeleteVersionAsync(string agentId, string versionId, CancellationToken ct = default);

    /// <summary>
    /// Desativa todas as versões do agente, fazendo o runtime voltar ao instructions base.
    /// Invalida o cache do agente.
    /// </summary>
    Task ClearMasterAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Remove a entrada do cache em memória para o agente especificado.
    /// Chamado automaticamente em todas as operações de escrita.
    /// </summary>
    void InvalidateCache(string agentId);
}
