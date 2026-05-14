using EfsAiHub.Core.Abstractions.Users;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Acesso ao usuário corrente via AsyncLocal — mesmo pattern de
/// TenantContextAccessor/ProjectContextAccessor. Permite que o usuário
/// resolvido pelo UserProvisioningMiddleware flua pra escopos internos
/// (background tasks, hosted services, código non-HTTP) sem depender de
/// HttpContextAccessor.
/// </summary>
public sealed class UserContextAccessor : IUserContextAccessor
{
    private static readonly AsyncLocal<User?> _ambient = new();

    public User? Current
    {
        get => _ambient.Value;
        set => _ambient.Value = value;
    }
}
