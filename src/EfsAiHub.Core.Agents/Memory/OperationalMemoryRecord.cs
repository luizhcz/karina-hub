using System.Text.Json;

namespace EfsAiHub.Core.Agents.Memory;

/// <summary>
/// Estado canônico que o agente mantém entre turnos de uma mesma conversa ou
/// sessão. Replace puro: cada update sobrescreve <see cref="Payload"/> inteiro
/// e bumpa <see cref="Version"/> em 1. <see cref="Version"/> existe pra
/// concorrência otimista — caller passa o valor lido como <c>expectedVersion</c>
/// no upsert e Postgres rejeita quando outra escrita já bumped.
/// </summary>
/// <remarks>
/// <see cref="Payload"/> é um <see cref="JsonElement"/> clonado (memória própria),
/// não um <see cref="JsonDocument"/> — caller pode armazenar livremente sem
/// dispose. Construir via <c>JsonDocument.Parse(...).RootElement.Clone()</c> ou
/// <c>JsonSerializer.SerializeToElement(obj)</c>.
/// </remarks>
public sealed record OperationalMemoryRecord
{
    public required string ProjectId { get; init; }
    public required string AgentId { get; init; }

    /// <summary><c>conversation</c> (chat) ou <c>session</c> (sandbox).</summary>
    public required string ScopeType { get; init; }

    public required string ScopeId { get; init; }

    /// <summary>Documento JSON que o LLM emite a cada turno. Default <c>{}</c> antes do primeiro write.</summary>
    public required JsonElement Payload { get; init; }

    public required int Version { get; init; }

    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
