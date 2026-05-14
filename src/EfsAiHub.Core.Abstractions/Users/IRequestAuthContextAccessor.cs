namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Carrega cabeçalhos de autenticação que viajam com a request mas não
/// pertencem à identidade resolvida (que vive em <see cref="IUserContextAccessor"/>).
///
/// Hoje o <c>UserProvisioningMiddleware</c> popula isso a partir dos headers
/// HTTP <c>app_origin</c> e <c>access_token</c>. Consumidores principais:
///   - <c>GenericToolExecutor</c> reenvia ambos pras chamadas HTTP downstream
///     (provedores de generic-tools usam pra autorizar — 401 = caller sem
///     permissão na ferramenta).
///   - Logs estruturados / audit poderão anexar como dimensões.
///
/// Quando o login virar JWT validado server-side, esta abstração fica como
/// ponto de extensão pra propagar claims adicionais sem mexer no contrato
/// do IUserIdentityProvider.
/// </summary>
public interface IRequestAuthContextAccessor
{
    /// <summary>Identifica o canal de origem do caller (ex.: web-mvp, mobile-ios).</summary>
    string? AppOrigin { get; set; }

    /// <summary>
    /// Token opaco do IdP do consumidor. Não validamos signature aqui — só
    /// repassamos pros sistemas downstream que validam contra o próprio JWKS.
    /// </summary>
    string? AccessToken { get; set; }
}
