namespace EfsAiHub.Core.Agents.Exceptions;

/// <summary>
/// Sinaliza que o agent referenciado está com <c>Enabled=false</c> e portanto
/// não tem handler runtime criado. Capturada por <c>BuildBindingMapAsync</c>
/// (modo Graph) pra skipar a chave do bindingMap — workflow continua execução
/// sem o agent (edges órfãs ignoradas).
/// </summary>
public sealed class AgentDisabledException : Exception
{
    public string AgentId { get; }

    public AgentDisabledException(string agentId)
        : base($"Agent '{agentId}' está desabilitado — não pode ser invocado.")
    {
        AgentId = agentId;
    }
}
