namespace EfsAiHub.Core.Abstractions.ChatSandbox;

/// <summary>
/// Sessão de teste isolado de um agente Conversational em chat AG-UI. Vive em
/// paralelo às implantações de produção: workflow Chat efêmero + conversation
/// real são criados pelo backend on-demand, com pin exato da versão do agente
/// (via x-version no trigger). Admin pode marcar a session como Validated, o
/// que popula colunas de validação em <c>agent_definitions</c> e libera o
/// agente pra ser plugado em chats reais sem warning.
/// </summary>
public sealed class ChatSandboxSession
{
    public required string ChatSandboxSessionId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentVersionId { get; init; }
    public required string WorkflowId { get; init; }
    public required string ConversationId { get; init; }
    public required string ProjectId { get; init; }
    public required string CreatedByUserId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastMessageAt { get; set; }
    public required DateTime ExpiresAt { get; init; }
    public ChatSandboxSessionStatus Status { get; set; } = ChatSandboxSessionStatus.Active;
    public DateTime? ValidatedAt { get; set; }
    public string? ValidatedByUserId { get; set; }
    public string? ValidationNotes { get; set; }
}

public enum ChatSandboxSessionStatus
{
    Active,
    Validated,
    Expired,
    Closed
}
