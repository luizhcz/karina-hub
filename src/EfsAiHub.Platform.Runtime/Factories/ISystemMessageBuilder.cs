using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Monta o conteúdo textual final do system message do agente — junta o prompt
/// base (invariante, cacheável pelo OpenAI) com seções voláteis (persona,
/// contexto da sessão) preservando o prefixo estável no topo.
///
/// Abstrair aqui (em vez de inline em AgentFactory) ganha:
///  1. Testabilidade — builder é puro, sem IO.
///  2. OCP — adicionar nova seção (ex: tools, context) futuramente não toca AgentFactory.
///  3. Invariante de cache documentado — a ordem das seções é responsabilidade desta classe.
/// </summary>
public interface ISystemMessageBuilder
{
    /// <summary>
    /// Monta o system message. <paramref name="personaPrompt"/> pode ser
    /// <see cref="ComposedPersonaPrompt.Empty"/> — neste caso o retorno é
    /// apenas o prompt base sem modificação.
    /// </summary>
    string Build(string agentInstructions, ComposedPersonaPrompt personaPrompt);
}
