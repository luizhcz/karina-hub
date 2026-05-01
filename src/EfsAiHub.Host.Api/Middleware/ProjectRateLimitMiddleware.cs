using EfsAiHub.Platform.Runtime.Guards;
using EfsAiHub.Core.Abstractions.Identity;
using StackExchange.Redis;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Middleware que aplica rate limiting por projeto.
/// Deve ser registrado APÓS ProjectMiddleware (que resolve o ProjectId).
/// Retorna 429 se rate limit excedido.
/// Se Redis estiver indisponível, adota estratégia fail-open (request liberado com warning).
/// </summary>
/// <remarks>
/// Política: budget é <b>SOFT/warning-only</b>. O check de
/// <c>MaxCostUsdPerDay</c>/<c>MaxTokensPerDay</c> que vivia aqui foi removido — o
/// <c>ProjectBudgetGuard</c> agora apenas observa e emite log/métrica via
/// <c>CheckAsync</c> (não bloqueia request). Ver <see cref="ProjectBudgetGuard"/>.
/// </remarks>
public sealed class ProjectRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProjectRateLimitMiddleware> _logger;

    public ProjectRateLimitMiddleware(RequestDelegate next, ILogger<ProjectRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IProjectContextAccessor projectAccessor,
        ProjectRateLimiter rateLimiter,
        ProjectBudgetGuard budgetGuard)
    {
        var projectId = projectAccessor.Current.ProjectId;

        // Admins acessando o projeto default são isentos de rate limit
        if (projectId == "default")
        {
            await _next(context);
            return;
        }

        // Rate limit check (429 Too Many Requests)
        // Fail-open: se Redis indisponível, loga warning e libera o request
        try
        {
            if (!await rateLimiter.TryAcquireAsync(projectId, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded for project.",
                    projectId,
                    retryAfterSeconds = 60
                });
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[RateLimit] Redis indisponível para projeto '{ProjectId}' — rate limiting desabilitado temporariamente.", projectId);
        }

        // Budget observation (não bloqueia — apenas log/métrica quando teto diário é cruzado).
        try
        {
            await budgetGuard.CheckAsync(projectId, context.RequestAborted);
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[BudgetGuard] Redis indisponível para projeto '{ProjectId}' — observação de budget pulada.", projectId);
        }

        await _next(context);
    }
}
