using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Personas;

/// <summary>
/// Implementação HTTP do <see cref="IPersonaProvider"/>. Consome API externa
/// configurada em <see cref="PersonaApiOptions"/>.
///
/// Contrato crítico: NUNCA lança. Qualquer falha (timeout, 404, 5xx, rede)
/// retorna <see cref="UserPersona.Anonymous"/> + log warning + métrica.
/// Política de recovery mora aqui (camada de transport), não em decorators
/// de cache a jusante.
/// </summary>
public sealed class HttpPersonaProvider : IPersonaProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly PersonaApiOptions _options;
    private readonly ILogger<HttpPersonaProvider> _logger;

    public HttpPersonaProvider(
        IHttpClientFactory httpFactory,
        IOptions<PersonaApiOptions> options,
        ILogger<HttpPersonaProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UserPersona> ResolveAsync(
        string userId,
        string userType,
        CancellationToken ct = default)
    {
        if (_options.Disabled || string.IsNullOrWhiteSpace(_options.BaseUrl))
            return UserPersona.Anonymous(userId, userType);

        if (string.IsNullOrWhiteSpace(userId))
            return UserPersona.AnonymousInstance;

        // 1 tentativa + 1 retry rápido — sem Polly pra não adicionar dep.
        // Mais que isso contamina p95 do chat; API externa mal comportada vira fallback.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return await FetchAsync(userId, userType, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento de usuário — propaga silencioso sem log.
                return UserPersona.Anonymous(userId, userType);
            }
            catch (Exception ex) when (attempt == 1)
            {
                _logger.LogDebug(ex,
                    "[PersonaApi] Falha transiente na tentativa 1 para user={UserId}. Retentando…",
                    userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[PersonaApi] Persona não resolvida para user={UserId}. Fallback Anonymous.",
                    userId);
            }
        }

        return UserPersona.Anonymous(userId, userType);
    }

    private async Task<UserPersona> FetchAsync(string userId, string userType, CancellationToken ct)
    {
        using var client = _httpFactory.CreateClient(nameof(HttpPersonaProvider));
        client.BaseAddress ??= new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);

        // Endpoint default assumido: GET {BaseUrl}/personas/{userId}?userType=X.
        // Se a API real expuser rota/contrato diferente, ajustar aqui + em PersonaApiDto.
        var path = $"personas/{Uri.EscapeDataString(userId)}?userType={Uri.EscapeDataString(userType)}";
        using var response = await client.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return UserPersona.Anonymous(userId, userType);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<PersonaApiDto>(cancellationToken: ct).ConfigureAwait(false);
        if (dto is null) return UserPersona.Anonymous(userId, userType);

        return new UserPersona(
            UserId: userId,
            UserType: userType,
            DisplayName: dto.DisplayName,
            Segment: dto.Segment,
            RiskProfile: dto.RiskProfile,
            AdvisorId: dto.AdvisorId);
    }

    // DTO espelha o payload da API externa. Mapeamento snake_case ↔ PascalCase
    // cobre convenção comum REST. Se API real usar outro formato, ajustar aqui.
    private sealed record PersonaApiDto(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("segment")] string? Segment,
        [property: JsonPropertyName("risk_profile")] string? RiskProfile,
        [property: JsonPropertyName("advisor_id")] string? AdvisorId);
}
