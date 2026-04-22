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
    /// <param name="resolvedBy">UserId capturado do header da request (x-efs-account/x-efs-user-profile-id).</param>
    public async Task ProcessApprovalsAsync(
        IReadOnlyList<AgUiInputMessage>? messages,
        string resolvedBy,
        CancellationToken ct = default)
    {
        if (messages is null) return;

        foreach (var msg in messages)
        {
            if (!string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase)) continue;
            if (msg.ToolCallId is null) continue;

            var approved = HitlResolutionClassifier.IsApproved(msg.Content);
            await _hitlService.ResolveAsync(
                msg.ToolCallId,
                msg.Content,
                resolvedBy: resolvedBy,
                approved: approved,
                ct: ct);
        }
    }
}
