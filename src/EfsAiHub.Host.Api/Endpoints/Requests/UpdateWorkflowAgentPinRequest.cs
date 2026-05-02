namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do endpoint <c>PATCH /api/workflows/{id}/agents/{agentId}/pin</c>. Atualiza
/// <c>WorkflowAgentReference.AgentVersionId</c> in-place. Caller pode opcionalmente
/// passar <c>Reason</c> pra contexto do audit (ex: "migrei pra v5 após review do
/// breaking change na schema do output").
/// </summary>
public class UpdateWorkflowAgentPinRequest
{
    public required string NewVersionId { get; init; }
    public string? Reason { get; init; }
}
