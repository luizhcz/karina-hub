namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Gating de visibilidade de projeto por usuário. Admin tem bypass total —
/// <see cref="GetVisibleProjectIdsAsync"/> retorna null pra sinalizar "todos
/// os projetos do tenant" sem materializar a lista. Non-admin recebe só o
/// set vinculado em aihub.user_projects.
///
/// Implementações devem cachear o resultado de <see cref="IsAuthorizedAsync"/>
/// por (externalUserId, projectId) com TTL curto (60s) pra absorver o custo
/// de adicionar um lookup por request no ProjectMiddleware.
/// </summary>
public interface IUserMembershipService
{
    /// <summary>
    /// Retorna a lista de project ids visíveis ao usuário. Null = admin
    /// (sem restrição — caller deve interpretar como "todos do tenant").
    /// Lista vazia = non-admin sem vínculo nenhum.
    /// </summary>
    Task<IReadOnlyList<string>?> GetVisibleProjectIdsAsync(string externalUserId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// True quando admin OU quando o non-admin tem vínculo com o projeto.
    /// Resultado cacheado por TTL curto.
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
    /// Limpa cache pra um par (tenant, externalUserId). Chamado quando a
    /// flag IsAdmin muda pra que a próxima request enxergue o estado novo
    /// sem esperar o TTL.
    /// </summary>
    void InvalidateForUser(string tenantId, string externalUserId);
}
