namespace EfsAiHub.Core.Orchestration.Workflows;

public interface IWorkflowEventBus
{
    /// <summary>Publica um evento para todos os subscribers de uma execução.</summary>
    Task PublishAsync(string executionId, WorkflowEventEnvelope eventEnvelope, CancellationToken ct = default);

    /// <summary>Inscreve-se para receber eventos de uma execução específica (SSE).</summary>
    IAsyncEnumerable<WorkflowEventEnvelope> SubscribeAsync(string executionId, CancellationToken ct = default);

    /// <summary>Retorna todos os eventos persistidos de uma execução (auditoria/replay).</summary>
    Task<IReadOnlyList<WorkflowEventEnvelope>> GetHistoryAsync(string executionId, CancellationToken ct = default);

    /// <summary>Retorna eventos persistidos de múltiplas execuções em batch (elimina N+1).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowEventEnvelope>>> GetHistoryBatchAsync(IEnumerable<string> executionIds, CancellationToken ct = default);
}

public class WorkflowEventEnvelope
{
    /// <summary>"token" | "step_completed" | "workflow_completed" | "hitl_required" | "error"</summary>
    public required string EventType { get; init; }
    public required string ExecutionId { get; init; }
    public required string Payload { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// ID auto-gerado da linha em workflow_event_audit.
    /// 0 para eventos não persistidos (tokens entregues apenas via NOTIFY).
    /// Usado como chave de deduplicação determinística no SubscribeAsync — elimina
    /// a possibilidade de colisão por EventType+Timestamp que existia na implementação anterior.
    /// </summary>
    public long SequenceId { get; init; } = 0;
}
