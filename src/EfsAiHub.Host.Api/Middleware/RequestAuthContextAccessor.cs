using EfsAiHub.Core.Abstractions.Users;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// AsyncLocal-backed pra fluir os cabeçalhos entre escopos async — mesmo
/// pattern de <see cref="TenantContextAccessor"/> / <see cref="UserContextAccessor"/>.
/// Permite que código non-HTTP (hosted services, GenericToolExecutor invocado
/// de dentro de um agente) ainda leia o estado da request HTTP original.
/// </summary>
public sealed class RequestAuthContextAccessor : IRequestAuthContextAccessor
{
    private static readonly AsyncLocal<string?> _appOrigin = new();
    private static readonly AsyncLocal<string?> _accessToken = new();

    public string? AppOrigin
    {
        get => _appOrigin.Value;
        set => _appOrigin.Value = value;
    }

    public string? AccessToken
    {
        get => _accessToken.Value;
        set => _accessToken.Value = value;
    }
}
