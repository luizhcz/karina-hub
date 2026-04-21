using EfsAiHub.Platform.Runtime.Guards;
using EfsAiHub.Core.Abstractions.Identity;
using StackExchange.Redis;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Middleware que aplica rate limiting e budget guard por projeto.
/// Deve ser registrado APÓS ProjectMiddleware (que resolve o ProjectId).
/// Retorna 429 se rate limit excedido, 402 se budget excedido.
/// Se Redis estiver indisponível, adota estratégia fail-open (request liberado com warning).
/// </summary>
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

        // Budget check (402 Payment Required)
        // Fail-open: se Redis indisponível, loga warning e libera o request
        try
        {
            var (allowed, reason) = await budgetGuard.CheckAsync(projectId, context.RequestAborted);
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = reason,
                    projectId
                });
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[BudgetGuard] Redis indisponível para projeto '{ProjectId}' — budget check desabilitado temporariamente.", projectId);
        }

        await _next(context);
    }
}
