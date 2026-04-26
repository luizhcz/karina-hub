using EfsAiHub.Host.Api.Chat.AgUi;

namespace EfsAiHub.Host.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseEfsMiddlewarePipeline(this WebApplication app)
    {
        // .NET 10 — UseHttpsRedirection ficou mais estrito: lança em ambientes sem
        // porta HTTPS configurada (dev + tests in-memory via WebApplicationFactory).
        // Aplica apenas em produção real (não em Development nem Testing), junto com HSTS.
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }
        app.UseCors();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.GlobalExceptionMiddleware>();
        app.UseMiddleware<EfsAiHub.Host.Api.Middleware.SecurityHeadersMiddleware>();
        // F8 — i18n: seta CultureInfo.CurrentUICulture por request via
        // Accept-Language (ou ConversationSession.Locale, quando futuro).
        // PersonaBooleanFormat lê daí pra renderizar "sim/não" vs "yes/no".
        app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
        {
            DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("pt-BR"),
            SupportedCultures = new[]
            {
                new System.Globalization.CultureInfo("pt-BR"),
                new System.Globalization.CultureInfo("en-US"),
            },
            SupportedUICultures = new[]
            {
                new System.Globalization.CultureInfo("pt-BR"),
                new System.Globalization.CultureInfo("en-US"),
            },
        });
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
