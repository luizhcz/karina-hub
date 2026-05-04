using System.ComponentModel.DataAnnotations;
using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/admin/predefined-models. Validação semântica
/// (Temperature em [0,2], MaxTokens > 0) roda no domain
/// <see cref="PredefinedModel.EnsureInvariants"/> e devolve 400.
/// </summary>
public sealed class CreatePredefinedModelRequest
{
    [Required, MaxLength(64)]
    public string Id { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    [MaxLength(4096)]
    public string Description { get; init; } = string.Empty;

    [Required, MaxLength(64)]
    public string Provider { get; init; } = string.Empty;

    [MaxLength(64)]
    public string? ClientType { get; init; }

    [MaxLength(512)]
    public string? Endpoint { get; init; }

    [Required, MaxLength(256)]
    public string DeploymentName { get; init; } = string.Empty;

    public float? DefaultTemperature { get; init; }

    public int? DefaultMaxTokens { get; init; }

    public bool Enabled { get; init; } = true;

    public PredefinedModel ToDomain() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = Description,
        Provider = Provider,
        ClientType = ClientType,
        Endpoint = Endpoint,
        DeploymentName = DeploymentName,
        DefaultTemperature = DefaultTemperature,
        DefaultMaxTokens = DefaultMaxTokens,
        Enabled = Enabled,
    };
}
