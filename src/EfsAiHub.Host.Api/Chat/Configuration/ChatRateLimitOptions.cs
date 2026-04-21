namespace EfsAiHub.Host.Api.Configuration;

public class ChatRateLimitOptions
{
    public const string SectionName = "ChatRateLimit";

    /// <summary>Máximo de mensagens por usuário na janela deslizante.</summary>
    public int MaxMessages { get; set; } = 10;

    /// <summary>Janela deslizante em segundos para limite por usuário.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Máximo de mensagens por conversa na janela deslizante.</summary>
    public int MaxMessagesPerConversation { get; set; } = 5;

    /// <summary>Janela deslizante em segundos para limite por conversa.</summary>
    public int ConversationWindowSeconds { get; set; } = 60;
}
