using System.Text.Json;

namespace EfsAiHub.Platform.Runtime.Services;

public sealed class McpHealthChecker : IMcpHealthChecker
{
    private const string HttpClientName = "McpHealthCheck";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpHealthChecker> _logger;

    public McpHealthChecker(IHttpClientFactory httpClientFactory, ILogger<McpHealthChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> CheckAsync(string serverUrl, string serverLabel, CancellationToken ct)
    {
        try
        {
            var healthUri = new Uri(new Uri(serverUrl), "/health");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = Timeout;

            var response = await client.GetAsync(healthUri, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MCP '{Label}' health check falhou: HTTP {Status} em {Url}.",
                    serverLabel, (int)response.StatusCode, healthUri);
                return $"MCP '{serverLabel}' indisponível: health check retornou HTTP {(int)response.StatusCode}.";
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var statusError = TryGetBodyStatusError(body, serverLabel);
            if (statusError is not null) return statusError;

            _logger.LogInformation("MCP '{Label}' health check OK em {Url}.", serverLabel, healthUri);
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("MCP '{Label}' health check timeout ({Timeout}s) em '{Url}'.",
                serverLabel, (int)Timeout.TotalSeconds, serverUrl);
            return $"MCP '{serverLabel}' indisponível: timeout ao verificar health.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MCP '{Label}' health check falhou: não foi possível conectar a '{Url}'.",
                serverLabel, serverUrl);
            return $"MCP '{serverLabel}' indisponível: {ex.Message}";
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "MCP '{Label}' URL inválida: '{Url}'.", serverLabel, serverUrl);
            return $"MCP '{serverLabel}' indisponível: URL inválida.";
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "MCP '{Label}' health check falhou por estado inválido.", serverLabel);
            return $"MCP '{serverLabel}' indisponível: configuração inválida.";
        }
    }

    private string? TryGetBodyStatusError(string body, string serverLabel)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var statusEl))
                return null;

            var status = statusEl.GetString();
            if (status is null) return null;

            if (status.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("healthy", StringComparison.OrdinalIgnoreCase))
                return null;

            _logger.LogWarning("MCP '{Label}' health check status: '{Status}'.", serverLabel, status);
            return $"MCP '{serverLabel}' reportou status '{status}'.";
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "MCP '{Label}' respondeu 2xx com body não-JSON — assumindo saudável.", serverLabel);
            return null;
        }
    }
}
