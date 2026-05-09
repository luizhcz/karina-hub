using System.Text.Json;
using EfsAiHub.Core.Agents.Memory;

namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Estado canônico de memória operacional pra um escopo. <see cref="Payload"/>
/// é o JSON completo emitido pelo LLM no último turno; <see cref="Version"/>
/// é incrementado a cada upsert (concorrência otimista).
/// </summary>
public sealed class OperationalMemoryResponse
{
    public required string AgentId { get; init; }
    public required string ScopeType { get; init; }
    public required string ScopeId { get; init; }
    public required JsonElement Payload { get; init; }
    public required int Version { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }

    public static OperationalMemoryResponse FromDomain(OperationalMemoryRecord r) => new()
    {
        AgentId = r.AgentId,
        ScopeType = r.ScopeType,
        ScopeId = r.ScopeId,
        Payload = r.Payload,
        Version = r.Version,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };
}
