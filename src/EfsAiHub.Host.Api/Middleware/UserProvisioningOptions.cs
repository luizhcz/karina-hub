namespace EfsAiHub.Host.Api.Configuration;

public class UserProvisioningOptions
{
    public const string SectionName = "UserProvisioning";

    /// <summary>
    /// Quando true, o UserProvisioningMiddleware NÃO faz UPSERT em
    /// aihub.users para requests cujo path começa com algum prefixo em
    /// <see cref="SkipPathPrefixes"/>. A resolução da identidade dos
    /// headers continua acontecendo — apenas a persistência é pulada.
    ///
    /// Default true: rotas externas (ex.: trigger AG-UI) não devem
    /// contaminar o diretório de usuários da plataforma. Pra reverter o
    /// comportamento sem deploy, basta setar false em config.
    /// </summary>
    public bool SkipAnonymousRoutes { get; set; } = true;

    /// <summary>
    /// Prefixos de path (match case-insensitive, StartsWith) cujos
    /// requests não disparam UPSERT em aihub.users mesmo trazendo
    /// headers de identidade válidos. Só é consultado quando
    /// <see cref="SkipAnonymousRoutes"/> está true.
    /// </summary>
    public List<string> SkipPathPrefixes { get; set; } = ["/api/aihub/chat/ag-ui"];
}
