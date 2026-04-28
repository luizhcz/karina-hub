using System.Text.Json;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Abstractions.Secrets;

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
        Sanitize(p.Settings),
        ProjectLlmConfigResponse.From(p.LlmConfig),
        p.Budget?.RootElement.Clone(),
        p.CreatedAt,
        p.UpdatedAt);

    // Apenas referências AWS Secrets Manager voltam verbatim. Literais legacy
    // (ainda presentes em registros antigos) retornam null — operador é forçado
    // a recadastrar via UI com uma referência válida.
    private static ProjectSettings Sanitize(ProjectSettings s)
    {
        if (s.Evaluation?.Foundry is not { } foundry) return s;
        var sanitized = foundry with
        {
            ApiKeyRef = SecretReference.IsAwsReference(foundry.ApiKeyRef) ? foundry.ApiKeyRef : null
        };
        return s with { Evaluation = s.Evaluation! with { Foundry = sanitized } };
    }
}

/// <summary>
/// LlmConfig exposto na resposta. Refs AWS Secrets Manager voltam verbatim em
/// <see cref="ProviderCredentialsResponse.SecretRef"/>.
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
            kvp =>
            {
                var apiKey = kvp.Value.ApiKey;
                var isAwsRef = !string.IsNullOrEmpty(apiKey) && SecretReference.IsAwsReference(apiKey);
                return new ProviderCredentialsResponse(
                    ApiKeySet: isAwsRef,
                    SecretRef: isAwsRef ? apiKey : null,
                    Endpoint: kvp.Value.Endpoint);
            });
        return new ProjectLlmConfigResponse(creds, config.DefaultModel, config.DefaultProvider);
    }
}

public sealed record ProviderCredentialsResponse(
    /// <summary>True quando há referência AWS Secrets Manager configurada.</summary>
    bool ApiKeySet,
    /// <summary>Referência AWS Secrets Manager (`secret://aws/...`).</summary>
    string? SecretRef,
    string? Endpoint);
