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
    /// Regex patterns (case-insensitive) cujos requests dispensam o header
    /// <c>x-efs-permissions</c>. Comportamento: se vier <c>x-efs-account</c> OU
    /// <c>x-efs-user-profile-id</c> SEM <c>x-efs-permissions</c>, o middleware
    /// trata como permissions vazia (<c>[]</c>) — caller fica identificado sem
    /// nenhum privilégio admin. Demais validações continuam:
    /// <list type="bullet">
    ///   <item>account + user-profile-id juntos → 400 (ambiguidade).</item>
    ///   <item>nenhum dos dois → anônimo (como hoje).</item>
    ///   <item>permissions sozinho sem account/profile → anônimo (ignorado).</item>
    /// </list>
    /// Usado pra integrações backend (webhooks, scripts) que sabem quem são
    /// (account) mas não conhecem o catálogo de permissions. Rotas admin
    /// continuam exigindo permission válida via AdminGate — sem account
    /// identificado, AdminGate rejeita.
    /// Patterns são compilados na primeira chamada e cacheados em memória.
    /// </summary>
    public List<string> PermissionsOptionalPathPatterns { get; set; } =
    [
        // Ingestões: tudo embaixo de /api/aihub/ingestions.
        @"^/api/aihub/ingestions(/.*)?$",
        // Standalone responses: /responses (POST) e /responses/{jobId} (GET).
        // NÃO casa /responses/{jobId}/deliveries (admin-only de webhook history).
        @"^/api/aihub/responses(/[^/]+)?$",
        // Workflow on-demand trigger. NÃO casa POST /workflows (criação) nem
        // PUT /workflows/{id} (edição) — ambos mantêm exigência de permissions.
        @"^/api/aihub/workflows/[^/]+/trigger$",
    ];
}
