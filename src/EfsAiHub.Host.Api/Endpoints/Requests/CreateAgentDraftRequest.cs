using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/agent-drafts. Aceita Id explícito (slug user-friendly que
/// vai persistir no agent canônico após publish) ou null (gera GUID provisório).
/// Payload aceita campos parciais — invariantes só rodam no publish.
/// </summary>
public sealed class CreateAgentDraftRequest
{
    public string? Id { get; init; }
    public AgentDraftPayload Payload { get; init; } = new();
}
