using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Host.Api.Chat.AgUi.Approval;

/// <summary>
/// Processa mensagens de aprovação (role=tool) enviadas pelo frontend via request AG-UI.
/// Substitui o endpoint /hitl: a resposta de aprovação chega inline no próximo /stream request,
/// como mensagem com role=tool e ToolCallId=interactionId.
/// </summary>
public sealed class AgUiApprovalMiddleware
{
    private readonly IHumanInteractionService _hitlService;

    public AgUiApprovalMiddleware(IHumanInteractionService hitlService)
    {
        _hitlService = hitlService;
    }

    /// <summary>
    /// Processa quaisquer mensagens de aprovação pendentes no request.
    /// Deve ser chamado antes do início da execução em StreamAsync.
    /// </summary>
    public void ProcessApprovals(IReadOnlyList<AgUiInputMessage>? messages)
    {
        if (messages is null) return;

        foreach (var msg in messages)
        {
            if (!string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase)) continue;
            if (msg.ToolCallId is null) continue;

            var approved = HitlResolutionClassifier.IsApproved(msg.Content);
            _hitlService.Resolve(msg.ToolCallId, msg.Content, approved);
        }
    }
}
