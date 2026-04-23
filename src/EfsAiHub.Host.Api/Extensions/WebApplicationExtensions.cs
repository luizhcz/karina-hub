using EfsAiHub.Host.Api.Chat.AgUi;

namespace EfsAiHub.Host.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseEfsMiddlewarePipeline(this WebApplication app)
    {
        app.UseHttpsRedirection();
        if (!app.Environment.IsDevelopment())
            app.UseHsts();
        app.UseCors();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.GlobalExceptionMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.SecurityHeadersMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.TenantMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.ProjectMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.DefaultProjectGuard>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.Identity.PersonaResolutionMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.ProjectRateLimitMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.AdminGateMiddleware>();
        app.MapControllers();
        app.MapAgUiEndpoints();

        return app;
    }

    public static WebApplication UseEfsHealthChecks(this WebApplication app)
    {
        // /health/live — sempre 200 (processo está vivo)
        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        });

        // /health/ready — 200 se Postgres e Redis OK, 503 se algum falhou
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = 200,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = 200,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = 503
            }
        });

        return app;
    }
}
