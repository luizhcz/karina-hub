using System.ComponentModel.DataAnnotations;
using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PUT /api/admin/predefined-models/{id}. <see cref="ExpectedUpdatedAt"/>
/// vem do GET anterior — divergência → 412. Id da rota prevalece.
/// </summary>
public sealed class UpdatePredefinedModelRequest
{
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

    [Required]
    public DateTime ExpectedUpdatedAt { get; init; }

    /// <summary>
    /// Materializa um patch sem Id/CreatedAt — service substitui pelos valores
    /// do row existente (não confia no client pra mudar identidade ou timestamps).
    /// </summary>
    public PredefinedModel ToDomainPatch() => new()
    {
        Id = string.Empty,
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
