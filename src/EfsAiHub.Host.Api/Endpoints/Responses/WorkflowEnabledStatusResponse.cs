namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Estado consolidado de habilitação do workflow. <c>Enabled = false</c> só
/// quando TODOS os agentes referenciados estão desabilitados (ou ausentes do
/// catálogo). Útil pra UI sinalizar implantações que não vão executar em runtime
/// — runtime pula agentes desabilitados via AgentFactory.
/// </summary>
public sealed class WorkflowEnabledStatusResponse
{
    public required bool Enabled { get; init; }
    public required int TotalAgents { get; init; }
    public required int EnabledAgents { get; init; }
    public required IReadOnlyList<WorkflowAgentEnabledItem> Agents { get; init; }
}

public sealed class WorkflowAgentEnabledItem
{
    public required string AgentId { get; init; }
    /// <summary>true=existe e habilitado, false=existe e desabilitado, null=agentId não encontrado no catálogo.</summary>
    public required bool? Enabled { get; init; }
}
