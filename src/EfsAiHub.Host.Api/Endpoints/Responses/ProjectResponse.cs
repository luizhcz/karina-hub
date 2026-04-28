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

    // Mascara apiKeyRef quando é literal (não começa com "secret://").
    // Mesmo padrão de ProjectLlmConfigResponse: chave nunca volta no response.
    private static ProjectSettings Sanitize(ProjectSettings s)
    {
        if (s.Evaluation?.Foundry is not { } foundry) return s;
        var masked = foundry with
        {
            ApiKeyRef = string.IsNullOrEmpty(foundry.ApiKeyRef)
                ? null
                : (foundry.ApiKeyRef.StartsWith("secret://", StringComparison.Ordinal)
                    ? foundry.ApiKeyRef
                    : "***")
        };
        return s with { Evaluation = s.Evaluation! with { Foundry = masked } };
    }
}

/// <summary>
/// LlmConfig exposto na resposta. Refs AWS Secrets Manager voltam verbatim em
/// <see cref="ProviderCredentialsResponse.SecretRef"/>; literais legacy DPAPI
/// não voltam (apenas <see cref="ProviderCredentialsResponse.LegacyDpapi"/> = true
/// pra UI mostrar nudge de recadastro).
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
                var hasKey = !string.IsNullOrEmpty(apiKey);
                var isAwsRef = hasKey && SecretReference.IsAwsReference(apiKey);
                return new ProviderCredentialsResponse(
                    ApiKeySet: hasKey,
                    SecretRef: isAwsRef ? apiKey : null,
                    LegacyDpapi: hasKey && !isAwsRef,
                    Endpoint: kvp.Value.Endpoint,
                    KeyVersion: kvp.Value.KeyVersion);
            });
        return new ProjectLlmConfigResponse(creds, config.DefaultModel, config.DefaultProvider);
    }
}

public sealed record ProviderCredentialsResponse(
    /// <summary>True se alguma credencial está configurada (AWS ref OU literal legacy).</summary>
    bool ApiKeySet,
    /// <summary>Referência AWS Secrets Manager (`secret://aws/...`) quando aplicável; null em legacy DPAPI.</summary>
    string? SecretRef,
    /// <summary>True quando a credencial ainda está em formato DPAPI legacy — UI deve mostrar nudge de recadastro.</summary>
    bool LegacyDpapi,
    string? Endpoint,
    string? KeyVersion);
