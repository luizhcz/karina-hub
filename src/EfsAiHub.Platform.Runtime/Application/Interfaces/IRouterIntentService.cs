using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Service do pool global de Router intents (cross-project por tenant).
/// Wrap CRUD do repositório + integração com o agent analyzer (workflow
/// <c>wf-router-intent-analyzer</c> no projeto <c>geral</c>) que detecta
/// conflitos e sugere <c>name</c>/<c>projectId</c> canônicos no save.
/// </summary>
public interface IRouterIntentService
{
    Task<RouterIntent> CreateAsync(string? id, RouterIntent draft, CancellationToken ct = default);
    Task<RouterIntent?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<RouterIntent>> ListAsync(CancellationToken ct = default);
    Task<RouterIntent> UpdateAsync(string id, RouterIntent patch, CancellationToken ct = default);

    /// <summary>
    /// Tenta deletar; lança <see cref="RouterIntentInUseException"/> se ainda
    /// referenciada por algum Router (FK RESTRICT). UI deve chamar
    /// <see cref="GetUsageAsync"/> antes pra preview do bloqueio.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Lista os Routers que referenciam essa intent. Vazio = pode deletar livre.</summary>
    Task<IReadOnlyList<RouterIntentUsage>> GetUsageAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Dispara o workflow analyzer com o pool corrente e a candidate. Retorna o
    /// <c>executionId</c> — caller polla <c>/executions/{id}</c> até
    /// <c>Completed</c> e parseia o output estruturado.
    /// </summary>
    Task<string> AnalyzeAsync(AnalyzeRouterIntentInput input, CancellationToken ct = default);
}

public sealed record AnalyzeRouterIntentInput(
    string Description,
    IReadOnlyList<string> Examples,
    string? NameHint,
    string? DisplayNameHint,
    string? ProjectIdHint,
    string? ExcludeId);
