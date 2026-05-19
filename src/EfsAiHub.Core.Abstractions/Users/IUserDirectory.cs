namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// CRUD do diretório de usuários (tabela aihub.users). Upsert é idempotente
/// por (ExternalUserId, TenantId) — chamado a cada request pelo
/// UserProvisioningMiddleware, mas protegido por cache em memória pra evitar
/// martelar DB. Diretório não armazena IsAdmin/Permissions — esses campos vêm
/// do header <c>x-efs-permissions</c> em cada request e são populados in-memory
/// pelo middleware.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Cria a row se ainda não existe, ou atualiza LastSeenAt+UserType+DisplayName
    /// se já existe. Retorna o User + flag <c>created</c> indicando
    /// se foi INSERT (true) ou UPDATE (false) — usado pro audit emitir
    /// <c>user.auto_provisioned</c> apenas na criação inicial.
    /// </summary>
    Task<UpsertResult> UpsertAsync(
        string externalUserId,
        string userType,
        string tenantId,
        string? displayName,
        CancellationToken ct = default);

    Task<User?> GetByExternalIdAsync(string externalUserId, string tenantId, CancellationToken ct = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Listagem paginada de usuários do tenant. <paramref name="search"/>
    /// filtra por ExternalUserId/DisplayName (LIKE case-insensitive). Order
    /// estável: LastSeenAt DESC, Id ASC. Total separado pra paginação UI.
    /// </summary>
    Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        string tenantId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task SetDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default);
}

/// <summary>Resultado do upsert: a row final + flag indicando se foi insert.</summary>
public sealed record UpsertResult(User User, bool Created);
