using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Host.Api.Models.Responses;

public sealed class PredefinedModelResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Provider { get; init; }
    public string? ClientType { get; init; }
    public string? Endpoint { get; init; }
    public required string DeploymentName { get; init; }
    public float? DefaultTemperature { get; init; }
    public int? DefaultMaxTokens { get; init; }
    public bool Enabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static PredefinedModelResponse FromDomain(PredefinedModel model) => new()
    {
        Id = model.Id,
        DisplayName = model.DisplayName,
        Description = model.Description,
        Provider = model.Provider,
        ClientType = model.ClientType,
        Endpoint = model.Endpoint,
        DeploymentName = model.DeploymentName,
        DefaultTemperature = model.DefaultTemperature,
        DefaultMaxTokens = model.DefaultMaxTokens,
        Enabled = model.Enabled,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };
}
