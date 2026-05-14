namespace EfsAiHub.Core.Abstractions.AgentSandbox;

/// <summary>
/// Sessão de teste isolado de um agente, sem deploy permanente. Backend cria
/// recurso efêmero on-demand (workflow Chat ou Standalone, dependendo do
/// <see cref="Mode"/>) com pin exato da versão do agente. Validation gate em
/// V1 é exclusivo de <c>Mode=chat</c> — Conversational validado libera plug
/// em chats de produção sem warning.
/// </summary>
public sealed class AgentSandboxSession
{
    public required string SandboxSessionId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentVersionId { get; init; }

    /// <summary>
    /// <c>chat</c> = workflow Chat efêmero (Conversational, AG-UI).
    /// <c>standalone</c> = workflow Standalone efêmero (Custom/Worker/ToolRunner).
    /// Backend deriva por <c>agent.Type</c> — caller não envia.
    /// </summary>
    public string Mode { get; init; } = AgentSandboxModes.Chat;

    public required string WorkflowId { get; init; }

    /// <summary>Null pra <c>Mode=standalone</c> (não cria conversation).</summary>
    public string? ConversationId { get; init; }

    public required string ProjectId { get; init; }
    public required string CreatedByUserId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastMessageAt { get; set; }
    public required DateTime ExpiresAt { get; init; }
    public AgentSandboxSessionStatus Status { get; set; } = AgentSandboxSessionStatus.Active;
    public DateTime? ValidatedAt { get; set; }
    public string? ValidatedByUserId { get; set; }
    public string? ValidationNotes { get; set; }
}

public enum AgentSandboxSessionStatus
{
    Active,
    Validated,
    Expired,
    Closed
}

/// <summary>Valores canônicos pro campo <see cref="AgentSandboxSession.Mode"/>.</summary>
public static class AgentSandboxModes
{
    public const string Chat = "chat";
    public const string Standalone = "standalone";
}
