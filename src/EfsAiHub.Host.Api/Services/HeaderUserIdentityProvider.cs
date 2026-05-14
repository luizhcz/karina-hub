using EfsAiHub.Core.Abstractions.Users;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Lê identidade dos headers HTTP — delega pro UserIdentityResolver clássico
/// pra preservar o comportamento atual (precedência de header, fallback SSE
/// via query param, validação de ambiguidade). Quando o login migrar pra
/// access_token, registrar um JwtUserIdentityProvider no lugar — o restante
/// do sistema consome IUserIdentityProvider e não sabe a origem da identidade.
/// </summary>
public sealed class HeaderUserIdentityProvider : IUserIdentityProvider
{
    private readonly UserIdentityResolver _resolver;

    public HeaderUserIdentityProvider(UserIdentityResolver resolver)
    {
        _resolver = resolver;
    }

    public UserIdentity? Resolve(HttpContext context, out string? errorMessage)
    {
        var resolved = _resolver.TryResolve(context.Request, out errorMessage);
        if (resolved is null) return null;
        return new UserIdentity(resolved.UserId, resolved.UserType);
    }
}
