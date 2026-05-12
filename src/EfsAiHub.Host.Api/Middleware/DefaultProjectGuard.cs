using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Bloqueia qualquer request cujo ProjectId resolvido seja "default" para usuários
/// não-administradores. Garante que o projeto "default" seja acessível apenas por admins.
/// A identidade é resolvida via <see cref="UserIdentityResolver"/> (suporta ambos os headers).
///
/// Rotas globais (não escopadas por projeto) são isentas:
///   - /api/aihub/agents/*        (definições de agente e prompts)
///   - /api/aihub/workflows/*     (definições de workflow)
///   - /api/aihub/chat/ag-ui/*    (integração de chat)
///   - /api/aihub/notifications/* (bell de notificações — visibility via HasQueryFilter)
///   - /dev                 (developer portal)
///
/// Deve ser registrado APÓS ProjectMiddleware (que resolve o ProjectId) e ANTES de
/// ProjectRateLimitMiddleware na pipeline.
/// Gate desabilitado quando <see cref="AdminOptions.AccountIds"/> for vazio (dev/test).
/// </summary>
public sealed class DefaultProjectGuard
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _adminAccountIds;
    private readonly UserIdentityResolver _identityResolver;

    public DefaultProjectGuard(RequestDelegate next, IOptions<AdminOptions> options, UserIdentityResolver identityResolver)
    {
        _next = next;
        _adminAccountIds = new HashSet<string>(options.Value.AccountIds, StringComparer.Ordinal);
        _identityResolver = identityResolver;
    }

    public async Task InvokeAsync(HttpContext context, IProjectContextAccessor accessor)
    {
        // Gate desabilitado (dev/test) ou projeto não é "default"
        if (_adminAccountIds.Count == 0 || accessor.Current.ProjectId != "default")
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

        // Projeto "default" requer identidade admin.
        // Para rotas SSE (path /stream), aceita query param fallback porque
        // EventSource no browser não envia headers customizados. Restrito a
        // /stream pra evitar vazar identidade em URLs de outras rotas.
        var path = context.Request.Path.Value ?? string.Empty;
        var isSseRoute = path.EndsWith("/stream", StringComparison.OrdinalIgnoreCase);
        var identity = isSseRoute
            ? _identityResolver.TryResolve(context.Request, out _)
            : _identityResolver.TryResolve(context.Request.Headers, out _);
        if (identity != null && _adminAccountIds.Contains(identity.UserId))
        {
            await _next(context);
            return;
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
