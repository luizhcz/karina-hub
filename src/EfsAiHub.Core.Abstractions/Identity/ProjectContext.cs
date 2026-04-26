namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Contexto de projeto resolvido por requisição/execução. Populado pelo middleware
/// no Host.Api e injetado em escopo para uso por query filters, throttlers e guards.
/// Irmão do <see cref="TenantContext"/> — um Project pertence a um Tenant.
/// </summary>
public sealed class ProjectContext
{
    public string ProjectId { get; }
    public string? ProjectName { get; }

    /// <summary>
    /// True se o contexto foi populado conscientemente (HTTP middleware, JWT claim, route param,
    /// ou fallback explícito). False apenas no <see cref="Default"/> sentinel retornado pelo
    /// accessor quando ninguém setou o <c>AsyncLocal</c> — sinal de caminho não-HTTP.
    /// <para>
    /// Guardrails per-projeto (BlocklistChatClient, AgentSessionService) checam essa flag
    /// pra fail-secure em caminhos onde o middleware não rodou (background jobs, scheduled tasks
    /// sem propagação correta de projectId).
    /// </para>
    /// </summary>
    public bool IsExplicit { get; }

    public ProjectContext(string projectId, string? projectName = null, bool isExplicit = true)
    {
        ProjectId = projectId;
        ProjectName = projectName;
        IsExplicit = isExplicit;
    }

    /// <summary>
    /// Sentinel retornado pelo <see cref="ProjectContextAccessor"/> quando ninguém populou
    /// o <c>AsyncLocal</c>. <c>IsExplicit=false</c> distingue isso do caso legítimo de HTTP
    /// request com fallback "default" (middleware seta IsExplicit=true ali).
    /// </summary>
    public static ProjectContext Default { get; } = new("default", "Default", isExplicit: false);
}
