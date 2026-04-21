using EfsAiHub.Host.Api.Services;

namespace EfsAiHub.Host.Api.Extensions;

/// <summary>
/// Phase 13 — DI do contexto Identity (resolução de identidade do usuário via headers).
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddHostApiIdentity(this IServiceCollection services)
    {
        services.AddSingleton<UserIdentityResolver>();
        return services;
    }
}
