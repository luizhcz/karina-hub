using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Personas;

/// <summary>
/// Implementação HTTP do <see cref="IPersonaProvider"/>. Faz branch por
/// <c>userType</c> e consome dois endpoints distintos — cada um retorna
/// um shape específico (cliente vs admin). Config em <see cref="PersonaApiOptions"/>.
///
/// Contrato crítico: NUNCA lança. Qualquer falha (timeout, 404, 5xx, rede)
/// retorna <see cref="UserPersonaFactory.Anonymous"/> do tipo correto +
/// log warning. Política de recovery mora aqui (camada de transport), não
/// em decorators de cache.
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
            return UserPersonaFactory.Anonymous(userId, userType);

        if (string.IsNullOrWhiteSpace(userId))
            return UserPersonaFactory.Anonymous(userId ?? "", userType);

        // 1 tentativa + 1 retry rápido. Sem Polly (evita dep). Mais que isso
        // contamina p95 do chat; API mal-comportada vira fallback Anonymous.
        //
        // Catches específicos: só exceptions de transport/schema/cancel viram
        // fallback Anonymous. Outras (NullRef, ArgumentException, etc.) propagam
        // — são bugs lógicos que não devem ser silenciados como "API instável".
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return userType switch
                {
                    UserPersonaFactory.ClienteUserType => await FetchClientAsync(userId, ct).ConfigureAwait(false),
                    UserPersonaFactory.AdminUserType => await FetchAdminAsync(userId, ct).ConfigureAwait(false),
                    _ => UserPersonaFactory.Anonymous(userId, userType),
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento do caller — propaga silencioso sem log nem retry.
                return UserPersonaFactory.Anonymous(userId, userType);
            }
            catch (System.Text.Json.JsonException ex)
            {
                // Schema drift / payload inválido: retry é desperdício (o payload
                // seguinte tende a ser idêntico). Fallback direto + log warning
                // pra evidenciar drift — admin precisa ver isso.
                _logger.LogWarning(ex,
                    "[PersonaApi] Payload inválido para user={UserId} ({UserType}). Fallback Anonymous sem retry.",
                    userId, userType);
                return UserPersonaFactory.Anonymous(userId, userType);
            }
            catch (Exception ex) when (IsTransientTransport(ex) && attempt == 1)
            {
                _logger.LogDebug(ex,
                    "[PersonaApi] Falha transiente na tentativa 1 para user={UserId} ({UserType}). Retentando…",
                    userId, userType);
            }
            catch (Exception ex) when (IsTransientTransport(ex))
            {
                _logger.LogWarning(ex,
                    "[PersonaApi] Persona não resolvida para user={UserId} ({UserType}). Fallback Anonymous.",
                    userId, userType);
            }
        }

        return UserPersonaFactory.Anonymous(userId, userType);
    }

    /// <summary>
    /// Falhas de transport que justificam retry: rede/DNS/TLS e timeout do HttpClient.
    /// JsonException NÃO entra aqui — é tratada separadamente sem retry (linha acima).
    /// Bugs lógicos (NullRef, ArgumentException, etc.) propagam pro middleware global
    /// como 500 ao invés de ser silenciados como "API instável".
    /// </summary>
    private static bool IsTransientTransport(Exception ex) => ex is
        HttpRequestException           // falha de rede / DNS / TLS
        or TaskCanceledException;      // timeout do HttpClient (!ct.IsCancellationRequested)

    private async Task<UserPersona> FetchClientAsync(string userId, CancellationToken ct)
    {
        using var client = BuildHttpClient();
        var path = $"{_options.ClientPath.TrimEnd('/')}/{Uri.EscapeDataString(userId)}";
        using var response = await client.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return ClientPersona.Anonymous(userId);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ClientPersonaDto>(cancellationToken: ct).ConfigureAwait(false);
        if (dto is null) return ClientPersona.Anonymous(userId);

        return new ClientPersona(
            UserId: userId,
            ClientName: dto.ClientName,
            SuitabilityLevel: dto.SuitabilityLevel,
            SuitabilityDescription: dto.SuitabilityDescription,
            BusinessSegment: dto.BusinessSegment,
            Country: dto.Country,
            IsOffshore: dto.IsOffshore ?? false);
    }

    private async Task<UserPersona> FetchAdminAsync(string userId, CancellationToken ct)
    {
        using var client = BuildHttpClient();
        var path = $"{_options.AdminPath.TrimEnd('/')}/{Uri.EscapeDataString(userId)}";
        using var response = await client.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return AdminPersona.Anonymous(userId);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<AdminPersonaDto>(cancellationToken: ct).ConfigureAwait(false);
        if (dto is null) return AdminPersona.Anonymous(userId);

        // Listas null → empty (contrato: IReadOnlyList nunca null no record).
        return new AdminPersona(
            UserId: userId,
            Username: dto.Username,
            PartnerType: dto.PartnerType,
            Segments: dto.Segments ?? Array.Empty<string>(),
            Institutions: dto.Institutions ?? Array.Empty<string>(),
            IsInternal: dto.IsInternal ?? false,
            IsWm: dto.IsWm ?? false,
            IsMaster: dto.IsMaster ?? false,
            IsBroker: dto.IsBroker ?? false);
    }

    private HttpClient BuildHttpClient()
    {
        var client = _httpFactory.CreateClient(nameof(HttpPersonaProvider));
        client.BaseAddress ??= new Uri(_options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);

        return client;
    }

    // DTOs espelham o payload camelCase da API externa. Campos nullable
    // porque API pode vir parcial (ex: admin novo sem institutions ainda).
    private sealed record ClientPersonaDto(
        [property: JsonPropertyName("clientName")] string? ClientName,
        [property: JsonPropertyName("suitabilityLevel")] string? SuitabilityLevel,
        [property: JsonPropertyName("suitabilityDescription")] string? SuitabilityDescription,
        [property: JsonPropertyName("businessSegment")] string? BusinessSegment,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("isOffshore")] bool? IsOffshore);

    private sealed record AdminPersonaDto(
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("partnerType")] string? PartnerType,
        [property: JsonPropertyName("segments")] IReadOnlyList<string>? Segments,
        [property: JsonPropertyName("institutions")] IReadOnlyList<string>? Institutions,
        [property: JsonPropertyName("isInternal")] bool? IsInternal,
        [property: JsonPropertyName("isWM")] bool? IsWm,
        [property: JsonPropertyName("isMaster")] bool? IsMaster,
        [property: JsonPropertyName("isBroker")] bool? IsBroker);
}
