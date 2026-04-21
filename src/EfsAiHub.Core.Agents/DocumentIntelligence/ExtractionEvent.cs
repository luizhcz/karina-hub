namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Evento de auditoria para rastreamento passo a passo do fluxo de extração.
/// </summary>
public record ExtractionEvent(Guid JobId, string EventType, string? Detail = null);
