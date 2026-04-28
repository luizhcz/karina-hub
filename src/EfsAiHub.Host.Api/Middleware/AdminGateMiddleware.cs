using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Bloqueia acesso a endpoints não-públicos para requests cuja identidade
/// (resolvida via <see cref="UserIdentityResolver"/>) não conste em
/// <see cref="AdminOptions.AccountIds"/>.
///
/// Endpoints públicos (sem restrição):
///   - /api/chat/ag-ui/*              (integração de chat)
///   - POST /api/workflows            (criar workflow)
///   - PUT  /api/workflows/{id}       (editar workflow — exatamente 3 segmentos)
///   - POST /api/agents               (criar agent)
///   - PUT  /api/agents/{id}          (editar agent — exatamente 3 segmentos)
///   - /api/conversations/*           (conversas — todos os métodos)
///   - GET  /api/users/{id}/conversations
///   - GET  /api/projects             (listar projetos)
///   - GET  /api/projects/{id}        (buscar projeto por ID)
///                                    (sub-rotas como /blocklist NÃO são públicas)
///
/// Retorna 403 Forbidden para demais endpoints sem account admin.
/// Gate desabilitado se <see cref="AdminOptions.AccountIds"/> for vazia.
/// </summary>
public sealed class AdminGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _adminAccountIds;
    private readonly UserIdentityResolver _identityResolver;

    // PUT /api/workflows/{id} — exatamente 3 segmentos (não inclui /rollback, /validate, etc.)
    private static readonly Regex WorkflowEditPattern =
        new(@"^/api/workflows/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // PUT /api/agents/{id} — exatamente 3 segmentos
    private static readonly Regex AgentEditPattern =
        new(@"^/api/agents/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/users/{userId}/conversations
    private static readonly Regex UserConversationsPattern =
        new(@"^/api/users/[^/]+/conversations$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/projects (lista) ou GET /api/projects/{id} (detalhe). Sub-rotas
    // como /api/projects/{id}/blocklist são admin-only — caem fora desse pattern.
    private static readonly Regex ProjectsReadPattern =
        new(@"^/api/projects(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AdminGateMiddleware(RequestDelegate next, IOptions<AdminOptions> options, UserIdentityResolver identityResolver)
    {
        _next = next;
        _adminAccountIds = new HashSet<string>(options.Value.AccountIds, StringComparer.Ordinal);
        _identityResolver = identityResolver;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Gate desabilitado (dev/test)
        if (_adminAccountIds.Count == 0)
        {
            await _next(context);
            return;
        }

        if (IsPublicRoute(context))
        {
            await _next(context);
            return;
        }

        // Rotas SSE (EventSource no browser não envia headers customizados):
        // aceita identidade via query param como fallback. Restrito a /stream
        // pra evitar vazar identidade em URLs de outras rotas.
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
            error = "Acesso negado. Este endpoint requer permissão de administrador."
        });
    }

    private bool IsPublicRoute(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Chat AG-UI — todos os métodos
        if (path.StartsWith("/api/chat/ag-ui", StringComparison.OrdinalIgnoreCase))
            return true;

        // Workflow: apenas criar (POST) e editar (PUT /{id})
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/workflows", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            && WorkflowEditPattern.IsMatch(path))
            return true;

        // Agent: apenas criar (POST) e editar (PUT /{id})
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/agents", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            && AgentEditPattern.IsMatch(path))
            return true;

        // Conversations — todos os métodos (chat via REST)
        if (path.StartsWith("/api/conversations", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/users/{userId}/conversations
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && UserConversationsPattern.IsMatch(path))
            return true;

        // Projects — apenas leitura (GET lista e GET por ID). Sub-rotas
        // (ex: /api/projects/{id}/blocklist, /api/projects/{id}/blocklist/violations)
        // permanecem admin-only — fora do regex acima.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ProjectsReadPattern.IsMatch(path))
            return true;

        // Enums — dados não-sensíveis, necessários para todos os clientes
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/enums", StringComparison.OrdinalIgnoreCase))
            return true;

        // Developer portal (dev-only, served via EmbeddedResource)
        if (path.Equals("/dev", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
