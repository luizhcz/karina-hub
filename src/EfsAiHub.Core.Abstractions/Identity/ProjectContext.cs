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

    public ProjectContext(string projectId, string? projectName = null)
    {
        ProjectId = projectId;
        ProjectName = projectName;
    }

    public static ProjectContext Default { get; } = new("default", "Default");
}
