using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Guards;
using EfsAiHub.Platform.Runtime.Guards;
using EfsAiHub.Platform.Runtime.Resilience;

namespace EfsAiHub.Host.Api.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            MetricsRegistry.UnhandledExceptions.Add(1,
                new KeyValuePair<string, object?>("path", context.Request.Path.Value),
                new KeyValuePair<string, object?>("method", context.Request.Method));

            if (!context.Response.HasStarted)
            {
                // Envelope custom pra blocklist — categoria visível, pattern_id NUNCA exposto.
                if (ex is BlocklistViolationException blockEx)
                {
                    context.Response.StatusCode = 422;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "policy_violation",
                        category = blockEx.Violation.Category,
                        violation_id = blockEx.Violation.ViolationId.ToString(),
                        retryable = false,
                        message = "Conteúdo violou política do projeto."
                    });
                    return;
                }

                context.Response.StatusCode = ex switch
                {
                    DomainException => 400,    // Invariante de domínio violada
                    ArgumentException => 400,
                    KeyNotFoundException => 404,
                    BudgetExceededException => 429,
                    CircuitOpenException => 503,
                    _ => 500
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = context.Response.StatusCode == 500
                        ? "Internal server error"
                        : ex.Message
                });
            }
        }
    }
}
