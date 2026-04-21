using System.Text.Json;
using EfsAiHub.Core.Abstractions.Projects;

namespace EfsAiHub.Host.Api.Models.Responses;

public sealed record ProjectResponse(
    string Id,
    string Name,
    string TenantId,
    string? Description,
    ProjectSettings Settings,
    ProjectLlmConfigResponse? LlmConfig,
    JsonElement? Budget,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProjectResponse From(Project p) => new(
        p.Id,
        p.Name,
        p.TenantId,
        p.Description,
        p.Settings,
        ProjectLlmConfigResponse.From(p.LlmConfig),
        p.Budget?.RootElement.Clone(),
        p.CreatedAt,
        p.UpdatedAt);
}

/// <summary>
/// LlmConfig exposto na resposta. ApiKey é sempre mascarada como "***" para
/// não vazar credenciais em logs ou UIs. Endpoint é exibido normalmente.
/// </summary>
public sealed record ProjectLlmConfigResponse(
    Dictionary<string, ProviderCredentialsResponse>? Credentials,
    string? DefaultModel,
    string? DefaultProvider)
{
    public static ProjectLlmConfigResponse? From(ProjectLlmConfig? config)
    {
        if (config is null) return null;
        var creds = config.Credentials.ToDictionary(
            kvp => kvp.Key,
            kvp => new ProviderCredentialsResponse(
                ApiKeySet: kvp.Value.ApiKey is not null,
                Endpoint: kvp.Value.Endpoint,
                KeyVersion: kvp.Value.KeyVersion));
        return new ProjectLlmConfigResponse(creds, config.DefaultModel, config.DefaultProvider);
    }
}

public sealed record ProviderCredentialsResponse(
    /// <summary>True se uma ApiKey foi configurada; a chave em si nunca é retornada.</summary>
    bool ApiKeySet,
    string? Endpoint,
    string? KeyVersion);
