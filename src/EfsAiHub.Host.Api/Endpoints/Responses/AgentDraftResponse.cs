using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Representação do draft retornada nos endpoints REST. Inclui status do
/// approval workflow + feedback de rejeição (quando aplicável) pra UI mostrar
/// inline. <see cref="IsEditDraft"/>/<see cref="BaseAgentId"/> distinguem fork
/// de criação.
/// </summary>
public sealed class AgentDraftResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AgentDraftPayload Payload { get; init; }
    public required string ProjectId { get; init; }
    public required string TenantId { get; init; }
    public string? BaseAgentId { get; init; }
    public int? BaseRevision { get; init; }
    public bool IsEditDraft { get; init; }
    public required string Status { get; init; }
    public string? RejectionFeedback { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? CreatedBy { get; init; }

    public static AgentDraftResponse FromDomain(AgentDraft draft) => new()
    {
        Id = draft.Id,
        Name = draft.Name,
        Payload = draft.Payload,
        ProjectId = draft.ProjectId,
        TenantId = draft.TenantId,
        BaseAgentId = draft.BaseAgentId,
        BaseRevision = draft.BaseRevision,
        IsEditDraft = draft.IsEditDraft,
        Status = draft.Status.ToString(),
        RejectionFeedback = draft.RejectionFeedback,
        SubmittedAt = draft.SubmittedAt,
        CreatedAt = draft.CreatedAt,
        UpdatedAt = draft.UpdatedAt,
        CreatedBy = draft.CreatedBy,
    };
}
