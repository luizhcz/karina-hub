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
        var user = userAccessor.Current;
        if (user is null || user.IsAdmin || resolved.ProjectId == "default")
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
