namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Usuário persistente do diretório (tabela aihub.users). Auto-provisionado
/// no primeiro request via UserProvisioningMiddleware. <see cref="IsAdmin"/>
/// e <see cref="Permissions"/> são in-memory only — populados pelo middleware
/// a partir do header <c>x-efs-permissions</c> e do <c>AdminPermissionEvaluator</c>,
/// nunca persistidos. Coluna boolean no DB foi removida; admin é derivado
/// de match contra <c>Admin:AdminPermissions</c> em config.
/// </summary>
public sealed class User
{
    public required Guid Id { get; init; }
    public required string ExternalUserId { get; init; }
    public required string UserType { get; init; }
    public required string TenantId { get; init; }
    public string? DisplayName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSeenAt { get; init; }

    /// <summary>
    /// Permissões enviadas pelo proxy no header <c>x-efs-permissions</c>.
    /// Não persistidas. Repopuladas a cada request pelo provisioning middleware
    /// (mesmo em cache hit) — proxy é fonte da verdade, não o DB.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Derivado de <see cref="Permissions"/> ∩ <c>Admin:AdminPermissions</c>.
    /// Calculado pelo provisioning middleware via <c>IAdminPermissionEvaluator</c>.
    /// Não persistido.
    /// </summary>
    public bool IsAdmin { get; init; }
}
