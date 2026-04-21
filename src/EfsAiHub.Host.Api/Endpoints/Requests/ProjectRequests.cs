using System.Text.Json;

namespace EfsAiHub.Host.Api.Models.Requests;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    ProjectSettingsInput? Settings,
    ProjectLlmConfigInput? LlmConfig,
    JsonElement? Budget);

public sealed record UpdateProjectRequest(
    string? Name,
    string? Description,
    ProjectSettingsInput? Settings,
    ProjectLlmConfigInput? LlmConfig,
    JsonElement? Budget);

public sealed record ProjectSettingsInput(
    string? DefaultProvider,
    string? DefaultModel,
    float? DefaultTemperature,
    int? MaxTokensPerDay,
    decimal? MaxCostUsdPerDay,
    int? MaxConcurrentExecutions,
    int? MaxRequestsPerMinute,
    int? MaxConversationsPerUser,
    bool? HitlEnabled,
    bool? BackgroundResponsesEnabled,
    int? MaxSandboxTokensPerDay);

/// <summary>
/// Configuração LLM enviada pelo cliente. ApiKey é plaintext na request
/// (obrigatório HTTPS); a cifragem acontece no repositório antes de persistir.
/// </summary>
public sealed record ProjectLlmConfigInput(
    Dictionary<string, ProviderCredentialsInput>? Credentials,
    string? DefaultModel,
    string? DefaultProvider);

public sealed record ProviderCredentialsInput(
    string? ApiKey,
    string? Endpoint);
