using EfsAiHub.Core.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Core.Agents.Interfaces;

/// <summary>
/// Cria IChatClient e AIAgent a partir de uma AgentDefinition.
/// Cada provider (OpenAI, AzureOpenAI, AzureFoundry) tem sua implementação.
/// </summary>
public interface ILlmClientProvider
{
    /// <summary>Identifica o provider suportado (ex: "OPENAI", "AZUREOPENAI", "AZUREFOUNDRY").</summary>
    string ProviderType { get; }

    /// <summary>Cria um agente completo do framework.</summary>
    Task<object> CreateAgentAsync(AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default);

    /// <summary>Cria um IChatClient direto (para Graph mode / LLM handler). Async porque resolve secrets via ISecretResolver (AWS).</summary>
    Task<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken ct = default);
}
