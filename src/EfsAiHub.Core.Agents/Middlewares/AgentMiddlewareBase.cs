using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Middlewares;

/// <summary>
/// Abstract base class that simplifies writing LLM middleware for agents.
/// Wraps <see cref="DelegatingChatClient"/> and exposes two async hooks so
/// junior developers do not need to know the underlying pipeline plumbing.
///
/// Usage — override only what you need:
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
/// Register the middleware via <c>IAgentMiddlewareRegistry.Register</c>:
/// <code>
/// registry.Register("MyLogging", (inner, agentId, settings, logger) =>
///     new MyLoggingMiddleware(inner, agentId, settings, logger));
/// </code>
/// </summary>
public abstract class AgentMiddlewareBase : DelegatingChatClient
{
    /// <summary>
    /// The identifier of the agent this middleware instance is attached to.
    /// </summary>
    protected string AgentId { get; }

    /// <summary>
    /// Read-only view of the settings configured for this middleware in the AgentDefinition.
    /// </summary>
    protected IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>
    /// Logger scoped to the concrete middleware type.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes the middleware base with all required dependencies.
    /// </summary>
    /// <param name="inner">The next <see cref="IChatClient"/> in the pipeline.</param>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="settings">Settings from the AgentDefinition middleware configuration.</param>
    /// <param name="logger">Logger instance provided by the runtime.</param>
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
    /// Called before the request is forwarded to the inner client.
    /// Override to inspect or mutate the message list (e.g. inject context, filter PII).
    /// Default implementation returns <paramref name="messages"/> unchanged.
    /// </summary>
    /// <param name="messages">The messages about to be sent to the LLM.</param>
    /// <param name="options">Chat options for this request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The (potentially modified) message sequence to forward.</returns>
    protected virtual Task<IEnumerable<ChatMessage>> OnBeforeRequestAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
        => Task.FromResult(messages);

    /// <summary>
    /// Called after the response is received from the inner client.
    /// Override to inspect or mutate the response (e.g. redact sensitive data, re-rank).
    /// Default implementation returns <paramref name="response"/> unchanged.
    /// </summary>
    /// <param name="response">The response returned by the LLM.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The (potentially modified) response.</returns>
    protected virtual Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response,
        CancellationToken ct)
        => Task.FromResult(response);

    /// <summary>
    /// Orchestrates the middleware pipeline: before-hook → inner client → after-hook.
    /// Can be further overridden when the hook pattern is insufficient.
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
