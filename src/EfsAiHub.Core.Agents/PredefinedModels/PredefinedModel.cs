using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Core.Agents.PredefinedModels;

/// <summary>
/// Receita curada de provider+deployment+temperatura+maxTokens com nome friendly
/// e descrição legível pra abstrair vocabulário técnico de PMs. Cadastrada por
/// admins no catálogo global; agents referenciam via
/// <c>AgentDefinition.Model.PredefinedModelId</c> e o runtime resolve live.
/// </summary>
public sealed class PredefinedModel
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string Provider { get; set; }
    public string? ClientType { get; set; }
    public string? Endpoint { get; set; }
    public required string DeploymentName { get; set; }
    public float? DefaultTemperature { get; set; }
    public int? DefaultMaxTokens { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new DomainException("PredefinedModel.Id é obrigatório.");
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new DomainException("PredefinedModel.DisplayName é obrigatório.");
        if (string.IsNullOrWhiteSpace(Provider))
            throw new DomainException("PredefinedModel.Provider é obrigatório.");
        if (string.IsNullOrWhiteSpace(DeploymentName))
            throw new DomainException("PredefinedModel.DeploymentName é obrigatório.");

        if (DefaultTemperature is float t && (t < 0f || t > 2f))
            throw new DomainException(
                "PredefinedModel.DefaultTemperature deve estar em [0, 2] quando presente.");

        if (DefaultMaxTokens is int m && m <= 0)
            throw new DomainException(
                "PredefinedModel.DefaultMaxTokens deve ser maior que zero quando presente.");
    }
}
