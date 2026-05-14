using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Bloqueia qualquer request cujo ProjectId resolvido seja "default" para usuários
/// não-administradores. Garante que o projeto "default" seja acessível apenas por admins.
/// A flag IsAdmin vem do <see cref="IUserContextAccessor"/>, populado pelo
/// <see cref="UserProvisioningMiddleware"/>.
///
/// Rotas globais (não escopadas por projeto) são isentas:
///   - /api/aihub/agents/*        (definições de agente e prompts)
///   - /api/aihub/workflows/*     (definições de workflow)
///   - /api/aihub/chat/ag-ui/*    (integração de chat)
///   - /api/aihub/notifications/* (bell de notificações — visibility via HasQueryFilter)
///   - /dev                 (developer portal)
///
/// Deve ser registrado APÓS ProjectMiddleware (que resolve o ProjectId) e
/// UserProvisioningMiddleware (que popula o IUserContextAccessor), ANTES de
/// ProjectRateLimitMiddleware na pipeline.
/// Gate desabilitado quando <see cref="AdminOptions.GateEnabled"/> for false (dev/test).
/// </summary>
public sealed class DefaultProjectGuard
{
    private readonly RequestDelegate _next;
    private readonly UserIdentityResolver _identityResolver;
    private readonly bool _gateEnabled;

    public DefaultProjectGuard(RequestDelegate next, IOptions<AdminOptions> options, UserIdentityResolver identityResolver)
    {
        _next = next;
        _identityResolver = identityResolver;
        _gateEnabled = options.Value.GateEnabled;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IProjectContextAccessor accessor,
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserDirectory directory)
    {
        // Gate desabilitado (dev/test) ou projeto não é "default"
        if (!_gateEnabled || accessor.Current.ProjectId != "default")
        {
            await _next(context);
            return;
        }

        // Rotas globais não são escopadas por projeto — isentas do guard
        if (IsGlobalRoute(context))
        {
            await _next(context);
            return;
        }

        // Caminho principal: UserProvisioningMiddleware já populou o accessor.
        if (userAccessor.Current?.IsAdmin == true)
        {
            await _next(context);
            return;
        }

        // Fallback SSE: EventSource no browser não envia headers customizados,
        // então o provisioning middleware não populou o accessor. Buscamos
        // identidade via query param e consultamos o diretório direto.
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/stream", StringComparison.OrdinalIgnoreCase))
        {
            var identity = _identityResolver.TryResolve(context.Request, out _);
            if (identity is not null)
            {
                var user = await directory.GetByExternalIdAsync(
                    identity.UserId, tenantAccessor.Current.TenantId, context.RequestAborted);
                if (user?.IsAdmin == true)
                {
                    await _next(context);
                    return;
                }
            }
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "O projeto 'default' requer permissão de administrador. Envie o header x-project-id com um projeto válido."
        });
    }

    /// <summary>
    /// Rotas que não são escopadas por projeto — recursos globais que não
    /// devem ser bloqueados pelo guard de projeto "default".
    /// </summary>
    private static bool IsGlobalRoute(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/api/aihub/agents", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/aihub/workflows", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/aihub/chat/ag-ui", StringComparison.OrdinalIgnoreCase))
            return true;
        // /me é público — non-admin sem projeto ainda precisa descobrir isAdmin.
        if (path.Equals("/api/aihub/me", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/aihub/notifications", StringComparison.OrdinalIgnoreCase))
            return true;
        // Catálogo público de presets — recurso global cross-tenant.
        if (path.StartsWith("/api/aihub/predefined-models", StringComparison.OrdinalIgnoreCase))
            return true;
        // Listagem/lookup de projetos é global (tenant-scoped). Controller filtra
        // o projeto 'default' pra non-admins (ver ProjectsController.List/GetById),
        // então liberar aqui não vaza nada — só permite o onboarding inicial
        // pegar a lista antes do user escolher um projectId.
        if (path.StartsWith("/api/aihub/projects", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/dev", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
