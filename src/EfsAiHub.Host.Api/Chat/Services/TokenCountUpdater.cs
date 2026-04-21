using EfsAiHub.Core.Abstractions.Conversations;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Atualiza o TokenCount de uma mensagem com o valor real de llm_token_usage.
/// Extraído do ConversationService para SRP: token tracking é cross-cutting.
/// </summary>
public class TokenCountUpdater
{
    private readonly ILlmTokenUsageRepository _tokenUsageRepo;
    private readonly IChatMessageRepository _msgRepo;
    private readonly ILogger<TokenCountUpdater> _logger;

    public TokenCountUpdater(
        ILlmTokenUsageRepository tokenUsageRepo,
        IChatMessageRepository msgRepo,
        ILogger<TokenCountUpdater> logger)
    {
        _tokenUsageRepo = tokenUsageRepo;
        _msgRepo = msgRepo;
        _logger = logger;
    }

    public void EnqueueUpdate(string messageId, string executionId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var tokenUsages = await _tokenUsageRepo.GetByExecutionIdAsync(executionId);
                var totalOutput = tokenUsages.Sum(t => t.OutputTokens);
                if (totalOutput > 0)
                    await _msgRepo.UpdateTokenCountAsync(messageId, totalOutput);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TokenCountUpdater] Falha ao atualizar TokenCount real para mensagem '{MsgId}'.",
                    messageId);
            }
        });
    }
}
