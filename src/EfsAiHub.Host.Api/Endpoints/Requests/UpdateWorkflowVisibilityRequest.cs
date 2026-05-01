namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PATCH /api/workflows/{id}/visibility — solicita mudança de visibilidade.
/// Reason é opcional; quando enviado vai pro audit log junto com payloadBefore/After.
/// </summary>
public sealed class UpdateWorkflowVisibilityRequest
{
    /// <summary>"project" | "global"</summary>
    public required string Visibility { get; init; }

    public string? Reason { get; init; }
}
