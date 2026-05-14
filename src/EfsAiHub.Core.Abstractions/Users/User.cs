namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Usuário persistente do diretório (tabela aihub.users). Auto-provisionado
/// no primeiro request via UserProvisioningMiddleware. <see cref="IsAdmin"/>
/// é a única fonte de verdade pra gating administrativo no runtime — config
/// de bootstrap só é usada no startup pra garantir admins iniciais.
/// </summary>
public sealed class User
{
    public required Guid Id { get; init; }
    public required string ExternalUserId { get; init; }
    public required string UserType { get; init; }
    public required string TenantId { get; init; }
    public string? DisplayName { get; init; }
    public required bool IsAdmin { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSeenAt { get; init; }
}
