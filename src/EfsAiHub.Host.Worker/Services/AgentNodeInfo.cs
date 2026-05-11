namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Metadata leve de um agente referenciado pelo workflow, indexado por
/// AgentId no <c>WorkflowRunnerService</c>. Encapsula <c>Name</c> + <c>Type</c>
/// pra discriminação no callback de nodes (agent vs executor) e pra
/// propagação do tipo no wire format AG-UI (<c>CUSTOM[agent.lifecycle]</c>).
/// </summary>
/// <param name="Name">Display name do agente — usado em telemetria/UI.</param>
/// <param name="Type">
/// String representando <c>AgentType</c> (Router | Conversational | Worker |
/// ToolRunner | Custom). Mantido como string pra evitar acoplar
/// <c>EfsAiHub.Host.Worker</c> ao enum do core; AG-UI já lida com string aqui.
/// </param>
public sealed record AgentNodeInfo(string Name, string Type);
