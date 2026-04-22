using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>
/// Entidade de projeto. Cada projeto pertence a um tenant e isola agentes,
/// workflows, conversas e execuções. O projeto "default" é criado automaticamente
/// para retrocompatibilidade.
/// </summary>
public class Project
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string TenantId { get; init; }
    public string? Description { get; set; }

    /// <summary>Configurações gerais do projeto (limites, defaults, feature flags).</summary>
    public ProjectSettings Settings { get; set; } = new();

    /// <summary>
    /// Configuração LLM do projeto: credenciais por provider (ApiKey plaintext no domínio)
    /// e modelo/provider default. A cifragem das ApiKeys é responsabilidade do repositório.
    /// </summary>
    public ProjectLlmConfig? LlmConfig { get; set; }

    /// <summary>Orçamento do projeto (custo diário, tokens diários).</summary>
    public JsonDocument? Budget { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Factory method validante — única forma correta de construir um <see cref="Project"/>
    /// com invariantes protegidas em código imperativo (controllers, application services).
    /// Para deserialização vinda de persistência use <c>new Project { ... }</c> e chame
    /// <see cref="EnsureInvariants"/> se quiser revalidar.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public static Project Create(
        string id,
        string name,
        string tenantId,
        string? description = null,
        ProjectSettings? settings = null,
        ProjectLlmConfig? llmConfig = null,
        JsonDocument? budget = null)
    {
        var project = new Project
        {
            Id = id,
            Name = name,
            TenantId = tenantId,
            Description = description,
            Settings = settings ?? new(),
            LlmConfig = llmConfig,
            Budget = budget
        };
        project.EnsureInvariants();
        return project;
    }

    /// <summary>
    /// Valida invariantes e lança <see cref="DomainException"/> se violadas.
    /// Idempotente. Chamado por <see cref="Create"/>; callers de persistência podem invocar explicitamente.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new DomainException("Project.Id é obrigatório.");
        if (string.IsNullOrWhiteSpace(TenantId))
            throw new DomainException("Project.TenantId é obrigatório.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Project.Name é obrigatório.");
        if (Settings.MaxCostUsdPerDay is < 0)
            throw new DomainException("ProjectSettings.MaxCostUsdPerDay deve ser >= 0 quando presente.");
        if (Settings.MaxTokensPerDay is < 0)
            throw new DomainException("ProjectSettings.MaxTokensPerDay deve ser >= 0 quando presente.");
    }
}
