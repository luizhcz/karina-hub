using System.ComponentModel.DataAnnotations;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/aihub/agent-approvals/{id}/reject. Feedback obrigatório (mín. 10
/// chars) — alimenta banner inline pro owner entender o que ajustar.
/// </summary>
public sealed class RejectAgentDraftRequest
{
    [Required]
    [MinLength(10, ErrorMessage = "Feedback deve ter pelo menos 10 caracteres.")]
    public required string Feedback { get; init; }
}
