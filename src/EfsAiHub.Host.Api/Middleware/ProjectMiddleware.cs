using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Resolve o projeto da request a partir do header <c>x-efs-project-id</c>,
/// JWT claim <c>project_id</c> ou route param. Popula o <see cref="IProjectContextAccessor"/>
/// scoped. Se ausente, mantém <see cref="ProjectContext.Default"/>.
///
/// Deve ser registrado APÓS o TenantMiddleware na pipeline.
/// </summary>
public sealed class ProjectMiddleware
{
    public const string ProjectHeader = "x-efs-project-id";
    private readonly RequestDelegate _next;

    public ProjectMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, IProjectContextAccessor accessor)
    {
        // 1. Header explícito (maior prioridade)
        if (context.Request.Headers.TryGetValue(ProjectHeader, out var headerValue))
        {
            var projectId = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                accessor.Current = new ProjectContext(projectId);
                return _next(context);
            }
        }

        // 2. JWT claim
        var claim = context.User.FindFirst("project_id");
        if (claim is not null && !string.IsNullOrWhiteSpace(claim.Value))
        {
            accessor.Current = new ProjectContext(claim.Value);
            return _next(context);
        }

        // 3. Route param
        if (context.Request.RouteValues.TryGetValue("projectId", out var routeValue) &&
            routeValue is string routeProjectId &&
            !string.IsNullOrWhiteSpace(routeProjectId))
        {
            accessor.Current = new ProjectContext(routeProjectId);
            return _next(context);
        }

        // 4. Fallback: 'default' (retrocompatível). Seta explicitamente pra que
        // ProjectContext.IsExplicit=true — guardrails distinguem este caso (HTTP sem header)
        // do caminho não-HTTP (AsyncLocal vazio → ProjectContext.Default com IsExplicit=false).
        accessor.Current = new ProjectContext("default", projectName: "Default", isExplicit: true);
        return _next(context);
    }
}
