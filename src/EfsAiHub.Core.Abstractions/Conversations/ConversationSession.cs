namespace EfsAiHub.Core.Abstractions.Conversations;

/// <summary>
/// Thread de chat entre um usuário e um workflow conversacional.
/// </summary>
public class ConversationSession
{
    public string ProjectId { get; init; } = "default";
    public required string ConversationId { get; init; }

    /// <summary>Identificador externo do usuário (vem do header x-efs-account ou x-efs-user-profile-id).</summary>
    public required string UserId { get; init; }

    /// <summary>"cliente" ou "admin" — derivado do header de autenticação usado.</summary>
    public string? UserType { get; init; }

    /// <summary>Workflow que processa as mensagens desta conversa.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Primeiras palavras da conversa, gerado automaticamente.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// ExecutionId da execução em curso.
    /// Nulo quando nenhuma execução está ativa.
    /// Quando não-nulo, uma nova mensagem do usuário pode ser um HITL response.
    /// </summary>
    public string? ActiveExecutionId { get; set; }

    /// <summary>
    /// Último agente que respondeu nesta conversa (ExecutorId do framework).
    /// Usado como otimização: na próxima execução do workflow Handoff,
    /// esse agente se torna o entry point (evita chamada extra ao manager).
    /// </summary>
    public string? LastActiveAgentId { get; set; }

    /// <summary>
    /// Quando definido, mensagens com createdAt &lt;= contextClearedAt são
    /// excluídas do ChatTurnContext enviado ao workflow (mas mantidas no banco para exibição).
    /// </summary>
    public DateTime? ContextClearedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}
