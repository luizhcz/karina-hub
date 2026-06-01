namespace EfsAiHub.Core.Agents.RouterQuickActions;

/// <summary>
/// Casa a mensagem do usuário com atalhos pré-cadastrados pra um Router.
/// Hit → bypass do LLM (output sintético determinístico).
/// Miss → null, fluxo LLM normal segue.
///
/// Implementação concreta carrega entries do repo em cache local (IMemoryCache
/// com TTL curto), normaliza pattern + content, e ordena por especificidade.
/// </summary>
public interface IRouterQuickActionMatcher
{
    Task<RouterQuickActionMatch?> TryMatchAsync(
        string routerId,
        string tenantId,
        string projectId,
        string userContent,
        CancellationToken ct = default);

    /// <summary>Invalida o cache local do Router — chamar após CRUD admin.</summary>
    void InvalidateRouter(string routerId, string tenantId, string projectId);
}

public sealed record RouterQuickActionMatch(string Intent, string Pattern, string DisplayText);
