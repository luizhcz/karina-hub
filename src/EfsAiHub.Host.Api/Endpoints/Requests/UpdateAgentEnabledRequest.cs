namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PATCH /api/agents/{id}/enabled — liga/desliga o agent. Reason é
/// opcional; quando enviado vai pro audit log junto com payloadBefore/After.
/// </summary>
public sealed class UpdateAgentEnabledRequest
{
    public required bool Enabled { get; init; }

    public string? Reason { get; init; }
}
