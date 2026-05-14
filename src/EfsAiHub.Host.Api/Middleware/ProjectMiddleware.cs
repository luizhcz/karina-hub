using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Resolve o projeto da request a partir do header <c>x-project-id</c>,
/// JWT claim <c>project_id</c> ou route param. Popula o <see cref="IProjectContextAccessor"/>
/// scoped. Se ausente, mantém <see cref="ProjectContext.Default"/>.
///
/// Após resolver, aplica ACL pra non-admin: se o usuário corrente não tem
/// vínculo com o projeto em aihub.user_projects, responde 403. Admin (bypass)
/// e requests sem identidade resolvida (rotas públicas) passam direto — o
/// AdminGate é quem bloqueia rotas sensíveis sem identidade.
///
/// Deve ser registrado APÓS TenantMiddleware e UserProvisioningMiddleware
/// (precisa de TenantId resolvido e IUserContextAccessor populado).
/// </summary>
public sealed class ProjectMiddleware
{
    public const string ProjectHeader = "x-project-id";
    private readonly RequestDelegate _next;

    public ProjectMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IProjectContextAccessor accessor,
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserMembershipService membership)
    {
        var resolved = ResolveProject(context);
        accessor.Current = resolved;

        // ACL não roda quando:
        //   - Não há usuário resolvido (rotas públicas como /me, /health, AG-UI).
        //     AdminGate é o segundo gate que bloqueia rotas sensíveis.
        //   - O usuário é admin (bypass total).
        //   - O projeto resolvido é o fallback "default" — DefaultProjectGuard
        //     já cuida desse caso (admin-only). Manter ACL aqui dispararia 403
        //     antes do guard rodar e quebraria /me pra non-admin sem projetos.
        //   - O endpoint não depende de contexto de projeto (bootstrap como
        //     /me, listagem global como /projects, rotas admin já gateadas por
        //     AdminGate). Sem essa exceção, identity stale em localStorage
        //     (projeto deletado/revogado) trava o frontend em loop de welcome:
        //     /me bate 403 → frontend cai no fallback isAdmin=false/projects=[]
        //     → RequireAccessOrWelcome redireciona pra /bem-vindo a cada
        //     refresh, sem caminho de recuperação.
        var user = userAccessor.Current;
        if (user is null || user.IsAdmin || resolved.ProjectId == "default" || IsProjectAgnosticRoute(context))
        {
            await _next(context);
            return;
        }

        // Non-admin sem vínculo no projeto explicitamente solicitado recebe
        // 403 com mensagem clara — caller deve trocar pra um projeto vinculado.
        var authorized = await membership.IsAuthorizedAsync(
            user.ExternalUserId,
            tenantAccessor.Current.TenantId,
            resolved.ProjectId,
            context.RequestAborted);

        if (!authorized)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Você não tem acesso ao projeto '{resolved.ProjectId}'. Solicite a um administrador o vínculo."
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Rotas que não operam sobre o contexto de projeto e portanto não devem
    /// ser bloqueadas por ACL mesmo quando o caller envia <c>x-project-id</c>
    /// stale. Endpoints aqui ou se autogovernam (ex.: <c>/projects</c> filtra
    /// pelo membership do user no controller) ou são gateados em outro lugar
    /// (<c>/admin/*</c> via AdminGate). Critério mínimo de entrada: o endpoint
    /// precisa funcionar quando o caller acabou de perder acesso ao projeto
    /// previamente selecionado, pra que o frontend consiga se reorientar.
    /// </summary>
    private static bool IsProjectAgnosticRoute(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.Equals("/api/aihub/me", StringComparison.OrdinalIgnoreCase))
            return true;

        // /projects e /projects/{id} — ProjectsController filtra pelo membership.
        // Sub-rotas (ex.: /projects/{id}/blocklist) NÃO entram aqui: precisam
        // do contexto pra autorização correta.
        if (path.Equals("/api/aihub/projects", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/aihub/projects/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["/api/aihub/projects/".Length..];
            if (!rest.Contains('/'))
                return true;
        }

        // Endpoints sob /admin/* são gateados por AdminGate (IsAdmin=true);
        // ACL de projeto não acrescenta nada aqui.
        if (path.StartsWith("/api/aihub/admin/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static ProjectContext ResolveProject(HttpContext context)
    {
        // 1. Header explícito (maior prioridade)
        if (context.Request.Headers.TryGetValue(ProjectHeader, out var headerValue))
        {
            var projectId = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(projectId))
                return new ProjectContext(projectId);
        }

        // 2. JWT claim
        var claim = context.User.FindFirst("project_id");
        if (claim is not null && !string.IsNullOrWhiteSpace(claim.Value))
            return new ProjectContext(claim.Value);

        // 3. Route param
        if (context.Request.RouteValues.TryGetValue("projectId", out var routeValue) &&
            routeValue is string routeProjectId &&
            !string.IsNullOrWhiteSpace(routeProjectId))
            return new ProjectContext(routeProjectId);

        // 4. Fallback: 'default' (retrocompatível). Seta explicitamente pra que
        // ProjectContext.IsExplicit=true — guardrails distinguem este caso (HTTP sem header)
        // do caminho não-HTTP (AsyncLocal vazio → ProjectContext.Default com IsExplicit=false).
        return new ProjectContext("default", projectName: "Default", isExplicit: true);
    }
}
