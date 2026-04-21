namespace EfsAiHub.Core.Abstractions.Events;

/// <summary>
/// Evento que precisa ser persistido append-only na trilha de auditoria.
/// Inclui metadados de correlação para reconstruir o trace de uma execução.
/// </summary>
public interface IAuditableEvent : IEvent
{
    Guid ExecutionId { get; }
    string? NodeId { get; }
    string CorrelationId { get; }
    string? CausationId { get; }
    string TenantId { get; }
}
