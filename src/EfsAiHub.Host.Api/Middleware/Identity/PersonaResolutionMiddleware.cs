using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Host.Api.Services;

namespace EfsAiHub.Host.Api.Middleware.Identity;

/// <summary>
/// Popula <see cref="IPersonaContextAccessor.Current"/> baseado no UserId
/// resolvido do header. Toda a lógica mora em <see cref="PersonaResolutionService"/>
/// (testável 100%); este middleware é glue do pipeline.
///
/// Posição: após <c>UserIdentityResolver</c> e antes de <c>ProjectRateLimitMiddleware</c>.
/// Skip paths: health/ready e swagger (não precisam de persona).
/// </summary>
public sealed class PersonaResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public PersonaResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        PersonaResolutionService service,
        IPersonaContextAccessor accessor)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await _next(context);
            return;
        }

        accessor.Current = await service.TryResolveAsync(context.Request.Headers, context.RequestAborted);
        await _next(context);
    }

    private static bool ShouldSkip(PathString path)
    {
        if (!path.HasValue) return false;
        var value = path.Value!;
        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
    }
}
