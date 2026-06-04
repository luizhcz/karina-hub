namespace EfsAiHub.Core.Abstractions.RouterQuickActions;

public interface IRouterQuickActionRepository
{
    Task<RouterQuickAction> CreateAsync(RouterQuickAction action, CancellationToken ct = default);

    Task<RouterQuickAction?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default);

    /// <summary>Lista todos os atalhos de um Router (cache layer faz call uma vez por TTL).</summary>
    Task<IReadOnlyList<RouterQuickAction>> ListByRouterAsync(
        string routerId, string tenantId, CancellationToken ct = default);

    Task<RouterQuickAction> UpdateAsync(RouterQuickAction action, CancellationToken ct = default);

    /// <summary>Retorna true se deletou.</summary>
    Task<bool> DeleteAsync(string id, string tenantId, CancellationToken ct = default);
}
