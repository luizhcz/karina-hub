using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Interfaces;

/// <summary>
/// Indica em qual fase do pipeline LLM o middleware atua.
/// </summary>
public enum MiddlewarePhase { Pre, Post, Both }

/// <summary>
/// Definição de uma setting configurável de middleware.
/// </summary>
public sealed class MiddlewareSettingDef
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = "text"; // "text" | "select"
    public List<MiddlewareSettingOption>? Options { get; init; }
    public string DefaultValue { get; init; } = "";
}

public sealed class MiddlewareSettingOption
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>
/// Metadata de um middleware registrado, retornada via API.
/// </summary>
public sealed class MiddlewareMetadata
{
    public string Type { get; init; } = "";
    public MiddlewarePhase Phase { get; init; }
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public List<MiddlewareSettingDef> Settings { get; init; } = [];
}

/// <summary>
/// Registry extensível de middlewares LLM por tipo de string.
/// Permite que times diferentes registrem seus próprios DelegatingChatClient
/// sem precisar alterar o AgentFactory.
///
/// Cada middleware é uma factory que recebe o client interno, o agentId,
/// as settings configuradas na AgentDefinition e um logger, e retorna
/// o IChatClient decorado.
///
/// Uso:
///   registry.Register("MeuMiddleware", MiddlewarePhase.Both, (inner, agentId, settings, logger) =>
///       new MeuChatClient(inner, agentId, settings, logger));
///
/// Ativação por agente: campo Middlewares na AgentDefinition com Type = "MeuMiddleware".
/// </summary>
public interface IAgentMiddlewareRegistry
{
    /// <summary>
    /// Registra uma factory para o tipo de middleware indicado, com metadata.
    /// Sobrescreve silenciosamente se o tipo já existir.
    /// </summary>
    void Register(
        string type,
        MiddlewarePhase phase,
        Func<IChatClient, string, Dictionary<string, string>, ILogger, IChatClient> factory,
        string? label = null,
        string? description = null,
        List<MiddlewareSettingDef>? settings = null);

    /// <summary>
    /// Tenta criar o middleware para o tipo indicado.
    /// Retorna false se o tipo não estiver registrado.
    /// </summary>
    bool TryCreate(
        string type,
        IChatClient inner,
        string agentId,
        Dictionary<string, string> settings,
        ILogger logger,
        out IChatClient result);

    /// <summary>
    /// Retorna os tipos de middleware registrados com suas fases, ordenados alfabeticamente.
    /// </summary>
    IReadOnlyCollection<(string Type, MiddlewarePhase Phase)> GetRegisteredTypes();

    /// <summary>
    /// Retorna metadata completa dos middlewares registrados.
    /// </summary>
    IReadOnlyCollection<MiddlewareMetadata> GetRegisteredMetadata();
}
