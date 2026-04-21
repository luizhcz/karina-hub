namespace EfsAiHub.Core.Agents;

/// <summary>
/// Representa uma versão de prompt de um agente.
/// </summary>
/// <param name="VersionId">Identificador da versão (ex: "v1.2").</param>
/// <param name="Content">Conteúdo do system prompt.</param>
/// <param name="IsActive">Indica se esta é a versão apontada pelo campo master.</param>
public record AgentPromptVersion(string VersionId, string Content, bool IsActive);
