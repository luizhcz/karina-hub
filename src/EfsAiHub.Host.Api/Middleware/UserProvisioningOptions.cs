namespace EfsAiHub.Host.Api.Configuration;

public class UserProvisioningOptions
{
    public const string SectionName = "UserProvisioning";

    /// <summary>
    /// Quando true, o UserProvisioningMiddleware é totalmente pulado para
    /// requests cujo path casa com algum prefixo em <see cref="SkipPathPrefixes"/>
    /// ou regex em <see cref="SkipPathPatterns"/>. Resolução de identidade,
    /// UPSERT em aihub.users e a validação obrigatória de <c>x-efs-permissions</c>
    /// ficam fora — endpoint trata como anônimo (ou re-resolve identity por
    /// conta própria, como o AG-UI stream faz).
    ///
    /// Default true: rotas externas (ex.: trigger AG-UI, ingestão por
    /// integração) não devem contaminar o diretório de usuários nem
    /// obrigar permissions header. Pra reverter sem deploy, basta setar false.
    /// </summary>
    public bool SkipAnonymousRoutes { get; set; } = true;

    /// <summary>
    /// Prefixos de path (match case-insensitive, StartsWith com boundary de
    /// segmento) cujos requests pulam o middleware inteiro. Match prefix é
    /// adequado quando TODA a sub-árvore deve ser pulada (ex.: <c>/api/aihub/chat/ag-ui</c>
    /// — qualquer sub-rota).
    /// </summary>
    public List<string> SkipPathPrefixes { get; set; } = ["/api/aihub/chat/ag-ui"];

    /// <summary>
    /// Regex patterns (case-insensitive) cujos requests pulam o middleware
    /// inteiro. Usar quando o prefix-match seria amplo demais — ex.: liberar
    /// <c>POST /api/aihub/workflows/{id}/trigger</c> sem afetar
    /// <c>POST /api/aihub/workflows</c> (criação admin-only), ou liberar
    /// <c>GET /api/aihub/responses/{jobId}</c> sem afetar
    /// <c>/responses/{jobId}/deliveries</c> (admin-only de webhook history).
    /// Patterns são compilados na primeira chamada do middleware e cacheados.
    /// </summary>
    public List<string> SkipPathPatterns { get; set; } =
    [
        // Ingestões: tudo embaixo de /api/aihub/ingestions (POST inicial,
        // futuros sub-paths).
        @"^/api/aihub/ingestions(/.*)?$",
        // Standalone responses: /responses (POST enqueue) e /responses/{jobId}
        // (GET polling). NÃO casa /responses/{jobId}/deliveries — esse mantém
        // a obrigação de identidade pra o defense-in-depth admin do controller.
        @"^/api/aihub/responses(/[^/]+)?$",
        // Workflow on-demand trigger: /workflows/{id}/trigger. Não casa POST
        // /workflows (criação) nem PUT /workflows/{id} (edição) — ambos
        // mantêm gate admin.
        @"^/api/aihub/workflows/[^/]+/trigger$",
    ];
}
