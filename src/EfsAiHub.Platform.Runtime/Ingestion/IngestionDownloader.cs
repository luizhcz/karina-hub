using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Resultado de um download via <see cref="IngestionDownloader"/>.
/// </summary>
public sealed record DownloadedFile(
    byte[] Bytes,
    string? ContentType,
    Uri FinalUrl,
    long ContentLength);

/// <summary>
/// Cliente HTTP especializado pra downloads externos do pipeline de ingestão.
/// Princípios:
/// <list type="bullet">
///   <item><c>AllowAutoRedirect=false</c> no HttpClient — redirect é manual no
///         loop deste downloader pra controle explícito sobre a cadeia.</item>
///   <item><c>MaxRedirects</c> de <see cref="IngestionApiOptions"/> limita cadeia.</item>
///   <item>Streaming com size cap (<c>MaxSizeBytes</c>) — abort mid-stream quando
///         ultrapassa.</item>
///   <item>Timeout total por chamada via CTS no caller (HttpClient com timeout
///         infinito) — mesma estratégia do <c>GenericToolExecutor</c>.</item>
/// </list>
///
/// O caller assume que a URL é GET idempotente: retomada do pipeline pode
/// re-baixar o mesmo recurso se um step falhar. URLs com side-effect em GET
/// (ex.: links assinados de uso único, endpoints transacionais) não são
/// suportadas — preferir blob storage estável.
/// </summary>
public interface IIngestionDownloader
{
    /// <summary>Baixa o recurso da URL (com redirects manuais e size cap). GET idempotente.</summary>
    Task<DownloadedFile> DownloadAsync(
        Uri url,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IIngestionDownloader"/>
public sealed class IngestionDownloader : IIngestionDownloader
{
    public const string HttpClientName = "ingestion-downloader";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<IngestionApiOptions> _options;
    private readonly ILogger<IngestionDownloader> _logger;

    public IngestionDownloader(
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionApiOptions> options,
        ILogger<IngestionDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<DownloadedFile> DownloadAsync(
        Uri url,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        var opts = _options.Value;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.DownloadTimeoutSeconds)));

        var currentUrl = url;
        for (var hop = 0; hop <= opts.MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            if (extraHeaders is not null)
            {
                foreach (var (k, v) in extraHeaders)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                        request.Headers.TryAddWithoutValidation(k, v);
                }
            }

            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location
                    ?? throw new IngestionRejectedException(
                        $"Redirect {(int)response.StatusCode} sem header Location.");

                // Redirects relativos precisam ser resolvidos contra a URL atual,
                // não a URL original (RFC 7231 §7.1.2).
                currentUrl = location.IsAbsoluteUri ? location : new Uri(currentUrl, location);
                if (hop == opts.MaxRedirects)
                {
                    throw new IngestionRejectedException(
                        $"Excedeu MaxRedirects={opts.MaxRedirects} em '{url}'.");
                }
                _logger.LogDebug("[IngestionDownloader] hop={Hop} redirect → {Url}", hop + 1, currentUrl);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IngestionRejectedException(
                    $"Download falhou status={(int)response.StatusCode} url='{currentUrl}'.");
            }

            var bytes = await ReadWithSizeCapAsync(response, opts.MaxSizeBytes, cts.Token)
                .ConfigureAwait(false);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            return new DownloadedFile(bytes, contentType, currentUrl, bytes.LongLength);
        }

        // Inalcançável — o loop sempre retorna ou lança.
        throw new InvalidOperationException("Loop de download terminou sem resultado.");
    }

    private static bool IsRedirect(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.MovedPermanently => true,
        System.Net.HttpStatusCode.Found => true,
        System.Net.HttpStatusCode.SeeOther => true,
        System.Net.HttpStatusCode.TemporaryRedirect => true,
        System.Net.HttpStatusCode.PermanentRedirect => true,
        _ => false,
    };

    /// <summary>
    /// Lê o body em chunks pra abortar antes de carregar arquivos enormes na
    /// memória. <paramref name="maxBytes"/> = 0 significa "sem limite" — ainda
    /// streamamos pra evitar ler tudo de uma vez, mas sem cap rígido.
    /// </summary>
    private static async Task<byte[]> ReadWithSizeCapAsync(
        HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var dst = new MemoryStream(capacity: 64 * 1024);
        var buffer = new byte[64 * 1024];
        long totalRead = 0;

        while (true)
        {
            var n = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) break;
            totalRead += n;
            if (maxBytes > 0 && totalRead > maxBytes)
            {
                throw new IngestionRejectedException(
                    $"Arquivo excede MaxSizeBytes={maxBytes} (lidos {totalRead} bytes antes do abort).");
            }
            dst.Write(buffer, 0, n);
        }

        return dst.ToArray();
    }
}
