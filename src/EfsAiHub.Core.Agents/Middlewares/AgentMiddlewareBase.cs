using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Middlewares;

/// <summary>
/// Classe base abstrata que simplifica escrever middleware LLM para agentes.
/// Envolve <see cref="DelegatingChatClient"/> e expõe dois hooks assíncronos para que
/// desenvolvedores juniores não precisem conhecer o encanamento interno do pipeline.
///
/// Uso — sobrescreva apenas o que precisar:
/// <code>
/// public class MyLoggingMiddleware : AgentMiddlewareBase
/// {
///     public MyLoggingMiddleware(IChatClient inner, string agentId,
///         Dictionary&lt;string, string&gt; settings, ILogger logger)
///         : base(inner, agentId, settings, logger) { }
///
///     protected override Task&lt;IEnumerable&lt;ChatMessage&gt;&gt; OnBeforeRequestAsync(
///         IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken ct)
///     {
///         Logger.LogInformation("Agent {Id}: sending {Count} messages", AgentId, messages.Count());
///         return Task.FromResult(messages);
///     }
///
///     protected override Task&lt;ChatResponse&gt; OnAfterResponseAsync(
///         ChatResponse response, CancellationToken ct)
///     {
///         Logger.LogInformation("Agent {Id}: received response", AgentId);
///         return Task.FromResult(response);
///     }
/// }
/// </code>
/// Registre o middleware via <c>IAgentMiddlewareRegistry.Register</c>:
/// <code>
/// registry.Register("MyLogging", (inner, agentId, settings, logger) =>
///     new MyLoggingMiddleware(inner, agentId, settings, logger));
/// </code>
/// </summary>
public abstract class AgentMiddlewareBase : DelegatingChatClient
{
    /// <summary>
    /// Identificador do agente ao qual esta instância de middleware está associada.
    /// </summary>
    protected string AgentId { get; }

    /// <summary>
    /// View read-only das settings configuradas para este middleware na AgentDefinition.
    /// </summary>
    protected IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>
    /// Logger com escopo no tipo concreto do middleware.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Inicializa a base do middleware com todas as dependências necessárias.
    /// </summary>
    /// <param name="inner">O próximo <see cref="IChatClient"/> no pipeline.</param>
    /// <param name="agentId">Identificador do agente.</param>
    /// <param name="settings">Settings do bloco de middleware na AgentDefinition.</param>
    /// <param name="logger">Instância de logger fornecida pelo runtime.</param>
    protected AgentMiddlewareBase(
        IChatClient inner,
        string agentId,
        Dictionary<string, string> settings,
        ILogger logger)
        : base(inner)
    {
        AgentId = agentId;
        Settings = settings.AsReadOnly();
        Logger = logger;
    }

    /// <summary>
    /// Chamado antes do request ser encaminhado ao client interno.
    /// Sobrescreva para inspecionar ou mutar a lista de mensagens (ex.: injetar contexto, filtrar PII).
    /// Implementação default retorna <paramref name="messages"/> sem alteração.
    /// </summary>
    /// <param name="messages">As mensagens prestes a serem enviadas ao LLM.</param>
    /// <param name="options">Opções de chat para este request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A sequência (potencialmente modificada) de mensagens a encaminhar.</returns>
    protected virtual Task<IEnumerable<ChatMessage>> OnBeforeRequestAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
        => Task.FromResult(messages);

    /// <summary>
    /// Chamado depois da resposta ser recebida do client interno.
    /// Sobrescreva para inspecionar ou mutar a resposta (ex.: redigir dados sensíveis, re-ranquear).
    /// Implementação default retorna <paramref name="response"/> sem alteração.
    /// </summary>
    /// <param name="response">A resposta retornada pelo LLM.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A resposta (potencialmente modificada).</returns>
    protected virtual Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response,
        CancellationToken ct)
        => Task.FromResult(response);

    /// <summary>
    /// Orquestra o pipeline do middleware: before-hook → client interno → after-hook.
    /// Pode ser sobrescrito quando o padrão de hooks não for suficiente.
    /// </summary>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var processedMessages = await OnBeforeRequestAsync(messages, options, cancellationToken);
        var response = await base.GetResponseAsync(processedMessages, options, cancellationToken);
        return await OnAfterResponseAsync(response, cancellationToken);
    }
}
