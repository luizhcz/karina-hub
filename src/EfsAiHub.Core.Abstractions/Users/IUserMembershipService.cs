namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Gating de visibilidade de projeto por usuário non-admin. Retorna o set
/// vinculado em aihub.user_projects. Bypass de admin é responsabilidade dos
/// callers (que conhecem a permission do request via <c>IUserContextAccessor</c>)
/// — service só responde sobre o vínculo concreto.
///
/// Implementações devem cachear o resultado de <see cref="IsAuthorizedAsync"/>
/// por (externalUserId, projectId) com TTL curto (60s) pra absorver o custo
/// de adicionar um lookup por request no ProjectMiddleware.
/// </summary>
public interface IUserMembershipService
{
    /// <summary>
    /// Retorna a lista de project ids vinculados ao usuário (lista vazia
    /// quando o caller ainda não tem nenhum vínculo). Não diferencia admin —
    /// chame só pelo caminho non-admin.
    /// </summary>
    Task<IReadOnlyList<string>> GetVisibleProjectIdsAsync(string externalUserId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// True quando o usuário tem vínculo concreto com o projeto. Resultado
    /// cacheado por TTL curto.
    /// </summary>
    Task<bool> IsAuthorizedAsync(string externalUserId, string tenantId, string projectId, CancellationToken ct = default);

    /// <summary>Lista vínculos de um usuário específico (usado pelo admin UI).</summary>
    Task<IReadOnlyList<string>> GetProjectsForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Substitui o set inteiro de vínculos do usuário. Não é additive —
    /// projetos ausentes em <paramref name="projectIds"/> são removidos.
    /// Idempotente. Invalida o cache automaticamente.
    /// </summary>
    Task AssignProjectsAsync(Guid userId, IReadOnlyList<string> projectIds, string actorExternalUserId, CancellationToken ct = default);

    /// <summary>
    /// Limpa cache pra um par (tenant, externalUserId). Chamado quando os
    /// vínculos mudam pra que a próxima request enxergue o estado novo sem
    /// esperar o TTL.
    /// </summary>
    void InvalidateForUser(string tenantId, string externalUserId);
}
