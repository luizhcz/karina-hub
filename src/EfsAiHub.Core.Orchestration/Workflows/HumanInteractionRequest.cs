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
}
