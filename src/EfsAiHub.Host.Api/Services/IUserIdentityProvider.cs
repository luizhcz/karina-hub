using EfsAiHub.Core.Abstractions.Users;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Ponto único de extração de identidade do request. Implementação atual
/// (<see cref="HeaderUserIdentityProvider"/>) lê headers x-efs-account /
/// x-efs-user-profile-id. Trocar pra access_token = registrar uma nova
/// implementação (ex.: JwtUserIdentityProvider) sem refactor amplo —
/// middlewares/controllers consomem esta interface, não a fonte de origem.
/// </summary>
public interface IUserIdentityProvider
{
    /// <summary>
    /// Retorna a identidade do caller ou null quando o request não trouxe
    /// identificação válida. <paramref name="errorMessage"/> descreve a causa
    /// quando relevante (ex.: dois headers simultâneos, ambíguo).
    /// </summary>
    UserIdentity? Resolve(HttpContext context, out string? errorMessage);
}
