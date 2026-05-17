using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Implementação isolada do tester. Separada do <see cref="GenericToolExecutor"/>
/// porque tem três diferenças deliberadas: (1) timeout fixo curto pra UX de teste,
/// (2) sem emissão de métricas — nao polui o dashboard de uso real, (3) sem
/// mascaramento de erro — devolve mensagem detalhada pro user diagnosticar.
/// Reusa <see cref="GenericRequestBuilder"/> e <see cref="GenericResponseParser"/>
/// pra garantir paridade de comportamento com o runtime real.
/// </summary>
public sealed class GenericToolTester : IGenericToolTester
{
    public const int TimeoutSeconds = 10;

    /// <summary>Limite de bytes do body capturado no envelope. Acima disso o body é truncado e a flag <c>ResponseTruncated</c> vai true.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    private const string ClientName = "generic-tool-tester";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GenericResponseProjector _projector;

    public GenericToolTester(
        IHttpClientFactory httpClientFactory,
        GenericResponseProjector projector)
    {
        _httpClientFactory = httpClientFactory;
        _projector = projector;
    }

    public async Task<GenericToolTestResult> TestAsync(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        string url;
        string method;
        string? requestBody = null;
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var request = GenericRequestBuilder.Build(tool, args);
            url = request.RequestUri?.ToString() ?? tool.UrlTemplate;
            method = request.Method.Method;

            CollectHeaders(request.Headers, requestHeaders);
            if (request.Content is not null)
            {
                CollectHeaders(request.Content.Headers, requestHeaders);
                // StringContent / FormUrlEncodedContent são bufferizados — ler aqui
                // não consome o stream e a request continua reenviável.
                requestBody = await request.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }

            var client = _httpClientFactory.CreateClient(ClientName);

            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

            var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectHeaders(response.Headers, responseHeaders);
            CollectHeaders(response.Content.Headers, responseHeaders);

            var (rawBody, truncated) = await ReadBodyAsync(response.Content, cts.Token).ConfigureAwait(false);

            object? parsed = null;
            string? parseError = null;
            if (response.IsSuccessStatusCode && !truncated && rawBody is not null)
            {
                try
                {
                    using var bodyContent = new StringContent(rawBody, Encoding.UTF8, MediaTypeForOutput(tool));
                    parsed = await GenericResponseParser
                        .ParseAsync(bodyContent, tool.OutputContentType, cts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception parseEx) when (parseEx is not OperationCanceledException)
                {
                    parseError = $"Falha ao parsear response como {tool.OutputContentType}: {parseEx.Message}";
                }
            }

            // Projection roda mesmo em modo Off (devolve bypass) pra padronizar
            // o envelope retornado ao tester. Aqui NÃO lançamos exception em
            // violation — só preenchemos SchemaErrors pra UI renderizar.
            var projection = parsed is not null
                ? _projector.Project(parsed, tool.OutputSchema, tool.OutputProjectionMode, tool.Name)
                : ProjectionResult.AsBypass(null);

            sw.Stop();

            return new GenericToolTestResult
            {
                Success = response.IsSuccessStatusCode && parseError is null,
                StatusCode = (int)response.StatusCode,
                DurationMs = sw.ElapsedMilliseconds,
                Url = url,
                Method = method,
                RequestBody = requestBody,
                RequestHeaders = requestHeaders,
                ResponseBody = rawBody,
                ResponseTruncated = truncated,
                ResponseHeaders = responseHeaders,
                ParsedData = parsed,
                ProjectedData = projection.HasErrors ? null : projection.Projected,
                SchemaErrors = projection.Errors,
                ProjectionBypassed = projection.Bypassed,
                Truncation = projection.Truncation,
                Error = parseError ?? (response.IsSuccessStatusCode
                    ? null
                    : $"Upstream retornou HTTP {(int)response.StatusCode} {response.ReasonPhrase}"),
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            return Failure(sw.ElapsedMilliseconds, $"Timeout após {TimeoutSeconds} segundos.", requestHeaders);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return Failure(sw.ElapsedMilliseconds, $"Falha de rede: {ex.Message}", requestHeaders);
        }
        catch (UriFormatException ex)
        {
            sw.Stop();
            return Failure(sw.ElapsedMilliseconds, $"URL inválida: {ex.Message}", requestHeaders);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Failure(sw.ElapsedMilliseconds, $"Erro inesperado: {ex.Message}", requestHeaders);
        }
    }

    private static GenericToolTestResult Failure(
        long durationMs,
        string error,
        Dictionary<string, string> requestHeaders) => new()
    {
        Success = false,
        StatusCode = null,
        DurationMs = durationMs,
        Url = string.Empty,
        Method = string.Empty,
        RequestBody = null,
        RequestHeaders = requestHeaders,
        ResponseBody = null,
        ResponseTruncated = false,
        ResponseHeaders = new Dictionary<string, string>(),
        ParsedData = null,
        ProjectedData = null,
        SchemaErrors = Array.Empty<string>(),
        ProjectionBypassed = true,
        Truncation = null,
        Error = error,
    };

    private static void CollectHeaders(HttpHeaders source, IDictionary<string, string> target)
    {
        foreach (var (key, values) in source)
            target[key] = string.Join(", ", values);
    }

    private static async Task<(string? Body, bool Truncated)> ReadBodyAsync(
        HttpContent content,
        CancellationToken ct)
    {
        // Lê tudo em memória até o limite. Não usamos ReadAsStringAsync direto
        // pra conseguir truncar em vez de carregar payloads gigantes inteiros.
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[8 * 1024];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            var remaining = MaxResponseBytes - (int)ms.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }
            ms.Write(buffer, 0, Math.Min(read, remaining));
            if (ms.Length >= MaxResponseBytes)
            {
                truncated = true;
                break;
            }
        }

        if (ms.Length == 0) return (null, truncated);
        var encoding = ResolveEncoding(content);
        return (encoding.GetString(ms.ToArray()), truncated);
    }

    private static Encoding ResolveEncoding(HttpContent content)
    {
        var charset = content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static string MediaTypeForOutput(GenericTool tool) => tool.OutputContentType switch
    {
        OutputContentType.Json => "application/json",
        OutputContentType.Text => "text/plain",
        OutputContentType.Csv => "text/csv",
        _ => "application/json",
    };
}
