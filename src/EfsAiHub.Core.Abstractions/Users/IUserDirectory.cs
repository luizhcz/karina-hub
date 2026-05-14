namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// CRUD do diretório de usuários (tabela aihub.users). Upsert é idempotente
/// por (ExternalUserId, TenantId) — chamado a cada request pelo
/// UserProvisioningMiddleware, mas protegido por cache em memória pra evitar
/// martelar DB. SetAdminAsync mantém o invariante do bootstrap: lookups
/// posteriores enxergam o estado novo sem precisar de reload.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Cria a row se ainda não existe, ou atualiza LastSeenAt+UserType+DisplayName
    /// se já existe. IsAdmin existente é preservado (não é tocado aqui — vide
    /// <see cref="SetAdminAsync"/>).
    /// </summary>
    Task<User> UpsertAsync(
        string externalUserId,
        string userType,
        string tenantId,
        string? displayName,
        CancellationToken ct = default);

    Task<User?> GetByExternalIdAsync(string externalUserId, string tenantId, CancellationToken ct = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Atualiza IsAdmin. Idempotente. Auditoria fica a cargo do caller —
    /// repository não escreve no admin_audit_log.
    /// </summary>
    Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default);

    Task SetDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default);
}
