using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Phase 9 — Resolve o tenant da request a partir do header <c>x-efs-tenant-id</c>
/// e popula o <see cref="ITenantContextAccessor"/> scoped. Se ausente, mantém
/// <see cref="TenantContext.Default"/>.
///
/// Enforcement no DbContext (HasQueryFilter sobre ITenantScoped) e RLS no Postgres
/// ficam documentados como débito — nenhuma entidade atual carrega TenantId,
/// portanto o filtro seria no-op até a coluna ser adicionada via migration.
/// </summary>
public sealed class TenantMiddleware
{
    public const string TenantHeader = "x-efs-tenant-id";
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, ITenantContextAccessor accessor)
    {
        if (context.Request.Headers.TryGetValue(TenantHeader, out var value))
        {
            var tenantId = value.ToString();
            if (!string.IsNullOrWhiteSpace(tenantId))
                accessor.Current = new TenantContext(tenantId);
        }
        return _next(context);
    }
}
