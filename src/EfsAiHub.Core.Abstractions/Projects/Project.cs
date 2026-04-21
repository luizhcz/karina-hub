using System.Text.Json;

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
}
