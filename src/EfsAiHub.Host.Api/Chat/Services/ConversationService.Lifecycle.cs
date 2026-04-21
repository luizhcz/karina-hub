using EfsAiHub.Core.Abstractions.Conversations;
using ChatMessage = EfsAiHub.Core.Abstractions.Conversations.ChatMessage;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Operações de ciclo de vida da conversa: criação, remoção e reset de contexto.
/// </summary>
public partial class ConversationService
{
    public async Task<ConversationSession> CreateAsync(
        string workflowId, string userId, string userType,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var session = new ConversationSession
        {
            ConversationId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            UserType = userType,
            WorkflowId = workflowId,
            ProjectId = _projectAccessor.Current.ProjectId,
            Metadata = metadata ?? []
        };
        return await _convRepo.CreateAsync(session, ct);
    }

    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        if (conversation is null)
            throw new KeyNotFoundException($"Conversa '{conversationId}' não encontrada.");

        if (!string.IsNullOrEmpty(conversation.ActiveExecutionId))
        {
            try
            {
                await _workflowService.CancelExecutionAsync(conversation.ActiveExecutionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ConvService] Falha ao cancelar execução '{ExecId}' ao deletar conversa '{ConvId}'.",
                    conversation.ActiveExecutionId, conversationId);
            }
        }

        var deleted = await _msgRepo.DeleteByConversationAsync(conversationId, ct);
        _logger.LogInformation("[ConvService] {Count} mensagens removidas da conversa '{ConvId}'.", deleted, conversationId);

        await _convRepo.DeleteAsync(conversationId, ct);
        _logger.LogInformation("[ConvService] Conversa '{ConvId}' deletada.", conversationId);
    }

    public async Task ClearContextAsync(string conversationId, CancellationToken ct = default)
    {
        var conversation = await _convRepo.GetByIdAsync(conversationId, ct);
        if (conversation is null) return;

        conversation.ContextClearedAt = DateTime.UtcNow;
        conversation.LastActiveAgentId = null; // Reseta otimização de entry point
        await _convRepo.UpdateAsync(conversation, ct);

        _logger.LogInformation("[ConvService] Contexto resetado para conversa '{ConvId}'.", conversationId);
    }
}
