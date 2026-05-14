namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Warning não-bloqueante emitido no save de Chat deploys quando um branch
/// agent Conversational não tem validation válida em Chat Sandbox. Frontend
/// renderiza inline mas não bloqueia o save (decisão de produto: warning,
/// não gate).
///
/// Códigos canônicos em <see cref="ChatValidationReasons"/>.
/// </summary>
public sealed record ChatValidationWarning(
    string AgentId,
    string AgentName,
    string Reason,
    string PinnedAgentVersionId,
    int? PinnedRevision,
    string? ValidatedAgentVersionId = null,
    int? ValidatedRevision = null);

public static class ChatValidationReasons
{
    /// <summary>Agente nunca foi validado em Chat Sandbox (validation columns NULL).</summary>
    public const string NoChatSandboxValidation = "no_chat_sandbox_validation";

    /// <summary>Validation existe mas pra versão diferente da pinada neste deploy.</summary>
    public const string ValidationStale = "validation_stale";
}
