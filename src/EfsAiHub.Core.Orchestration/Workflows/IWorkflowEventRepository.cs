
namespace EfsAiHub.Core.Orchestration.Workflows;

public interface IWorkflowEventRepository
{
    /// <summary>Persiste o envelope e retorna o ID auto-gerado da linha criada.</summary>
    Task<long> AppendAsync(WorkflowEventEnvelope envelope, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowEventEnvelope>> GetAllAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowEventEnvelope>>> GetAllByExecutionIdsAsync(IEnumerable<string> executionIds, CancellationToken ct = default);

    /// <summary>Busca um evento pelo SequenceId (PK). Usado pelo outbox para resolver referências do NOTIFY.</summary>
    Task<WorkflowEventEnvelope?> GetBySequenceIdAsync(long sequenceId, CancellationToken ct = default);
}
