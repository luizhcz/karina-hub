using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Core.Orchestration.Workflows;

public class HumanInteractionRequest
{
    public required string InteractionId { get; init; }
    public required string ExecutionId { get; init; }
    public required string WorkflowId { get; init; }

    /// <summary>Pergunta ou texto de aprovação apresentado ao humano</summary>
    public required string Prompt { get; init; }

    /// <summary>Contexto da conversa do agente até o momento</summary>
    public string? Context { get; init; }

    /// <summary>Tipo da interação (Approval, Input, Choice). Default: Approval.</summary>
    public InteractionType InteractionType { get; init; } = InteractionType.Approval;

    /// <summary>
    /// Opções disponíveis para Choice/Approval (ex: ["Confirmar", "Cancelar"]).
    /// Null para Input (texto livre) ou quando não aplicável.
    /// </summary>
    public IReadOnlyList<string>? Options { get; init; }

    public HumanInteractionStatus Status { get; set; } = HumanInteractionStatus.Pending;

    /// <summary>Resposta ou decisão do humano</summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Timeout independente do HITL em segundos. Se > 0, o backend expira a interação
    /// automaticamente após esse tempo, independente do workflow timeout.
    /// Valor 0 ou negativo = sem timeout independente (governa o workflow timeout).
    /// </summary>
    public int TimeoutSeconds { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// UserId de quem resolveu a interação (x-efs-account ou x-efs-user-profile-id
    /// capturado do caller). Para expiração automática por timeout interno do service,
    /// usar a constante <c>HitlActors.SystemTimeout</c> ("system:timeout").
    /// Null enquanto <see cref="Status"/> == Pending.
    /// </summary>
    public string? ResolvedBy { get; set; }
}

/// <summary>
/// Valores convencionais para <see cref="HumanInteractionRequest.ResolvedBy"/>
/// quando a resolução é acionada por código do sistema (não por humano).
/// </summary>
public static class HitlActors
{
    /// <summary>Resolução automática por expiração do timeout interno do service.</summary>
    public const string SystemTimeout = "system:timeout";
}
