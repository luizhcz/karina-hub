using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Worker que entrega webhooks de jobs standalone. Cada iteração:
/// <list type="number">
///   <item>Lease N rows Status='Pending' em <c>webhook_deliveries</c> via
///         <c>FOR UPDATE SKIP LOCKED</c>.</item>
///   <item>Pra cada delivery: POST com HMAC-SHA256 (header
///         <c>X-EfsAiHub-Signature: sha256={hex}</c>) assinado sobre o raw
///         body; 2xx → <c>Delivered</c>, qualquer outra resposta → <c>Failed</c>.</item>
/// </list>
///
/// SEM retry no design atual — uma tentativa só. Cliente reconcilia via
/// <c>GET /responses/{jobId}</c> se perdeu o webhook.
///
/// Sem SsrfGuard (decisão consciente, mesma postura do
/// <c>IngestionDownloader</c>). Recomendado ligar apenas em ambientes onde a
/// URL do callback é confiável (callbacks internos, parceiros conhecidos).
/// </summary>
public sealed class WebhookCallbackDeliveryService : BackgroundService
{
    public const string HttpClientName = "webhook-callback-delivery";
    private const string SignatureHeader = "X-EfsAiHub-Signature";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackgroundResponseRepository _jobs;
    private readonly WebhookDeliveryOptions _options;
    private readonly ILogger<WebhookCallbackDeliveryService> _logger;
    private readonly SemaphoreSlim _concurrencyGate;

    public WebhookCallbackDeliveryService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IBackgroundResponseRepository jobs,
        IOptions<WebhookDeliveryOptions> options,
        ILogger<WebhookCallbackDeliveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
        var maxConcurrent = Math.Max(1, _options.MaxConcurrentDeliveries);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[WebhookDelivery] Desligado (WebhookDelivery:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "[WebhookDelivery] Ativo batchSize={Batch} timeout={Timeout}s idle={Idle}s maxConcurrent={Conc}",
            _options.BatchSize, _options.DeliveryTimeoutSeconds, _options.PollIdleSeconds,
            _options.MaxConcurrentDeliveries);

        // Sweep no startup: rows que ficaram em Delivering depois do pod morrer
        // são promovidas pra Failed antes do loop normal começar.
        await SweepStuckDeliveriesAsync(stoppingToken).ConfigureAwait(false);

        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.PollIdleSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                if (processed == 0)
                    await Task.Delay(idle, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebhookDelivery] Falha no loop. Aplicando backoff.");
                try { await Task.Delay(idle, stoppingToken).ConfigureAwait(false); } catch { /* shutdown */ }
            }
        }
    }

    private async Task<int> PollOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var deliveries = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();

        var pending = await deliveries.LeasePendingAsync(_options.BatchSize, ct).ConfigureAwait(false);
        if (pending.Count == 0) return 0;

        foreach (var delivery in pending)
        {
            // Cada delivery processa fire-and-forget, mas o semáforo limita
            // quantos rodam em paralelo. Fila com 1000 pending não satura o
            // thread pool nem o HttpClient — excesso aguarda dentro do worker.
            _ = Task.Run(async () =>
            {
                await _concurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                try { await DeliverAsync(delivery, ct).ConfigureAwait(false); }
                finally { _concurrencyGate.Release(); }
            }, ct);
        }

        return pending.Count;
    }

    private async Task SweepStuckDeliveriesAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var deliveries = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();
            var threshold = TimeSpan.FromSeconds(Math.Max(1, _options.DeliveringTimeoutSeconds));
            var reclaimed = await deliveries.ReclaimStuckDeliveringAsync(threshold, ct).ConfigureAwait(false);
            if (reclaimed > 0)
            {
                _logger.LogWarning(
                    "[WebhookDelivery] Sweep promoveu {Count} delivery(s) órfã(s) (Delivering > {Threshold}s) pra Failed.",
                    reclaimed, _options.DeliveringTimeoutSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookDelivery] Sweep de órfãos falhou no startup. Loop normal segue.");
        }
    }

    private async Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        // Scope próprio por delivery — repository pode ser scoped/transient.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var deliveries = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();

        var sw = Stopwatch.StartNew();
        string outcome = "failed";

        try
        {
            var job = await _jobs.GetAsync(delivery.JobId, ct).ConfigureAwait(false);
            if (job is null)
            {
                await deliveries.MarkFailedAsync(delivery.DeliveryId, "Job não encontrado.", null, ct).ConfigureAwait(false);
                return;
            }

            var payload = BuildPayload(job);
            var rawBody = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);

            using var content = new ByteArrayContent(rawBody);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Url) { Content = content };

            // HMAC-SHA256 sobre o raw body. Quando HmacSecret é null, header não é enviado —
            // cliente decide se exige assinatura ou aceita unsigned (descobrindo via 401/4xx).
            if (!string.IsNullOrWhiteSpace(delivery.HmacSecret))
            {
                var signature = ComputeHmac(delivery.HmacSecret, rawBody);
                request.Headers.TryAddWithoutValidation(SignatureHeader, $"sha256={signature}");
            }

            if (delivery.Headers is not null)
            {
                foreach (var (k, v) in delivery.Headers)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    request.Headers.TryAddWithoutValidation(k, v);
                }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.DeliveryTimeoutSeconds)));

            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                outcome = "delivered";
                await deliveries.MarkDeliveredAsync(delivery.DeliveryId, code, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "[WebhookDelivery] Delivered job={JobId} delivery={DeliveryId} url={Url} code={Code}",
                    delivery.JobId, delivery.DeliveryId, delivery.Url, code);
            }
            else
            {
                await deliveries.MarkFailedAsync(delivery.DeliveryId, $"HTTP {code}", code, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogWarning(
                    "[WebhookDelivery] Failed job={JobId} delivery={DeliveryId} url={Url} code={Code}",
                    delivery.JobId, delivery.DeliveryId, delivery.Url, code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — delivery fica em Status='Delivering'. Sweep do
            // próximo startup do worker (linha SweepStuckDeliveriesAsync)
            // promove pra Failed após DeliveringTimeoutSeconds.
        }
        catch (Exception ex)
        {
            try
            {
                await deliveries.MarkFailedAsync(delivery.DeliveryId, ex.Message, null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { /* engole */ }
            _logger.LogWarning(ex, "[WebhookDelivery] Exception job={JobId} delivery={DeliveryId} url={Url}",
                delivery.JobId, delivery.DeliveryId, delivery.Url);
        }
        finally
        {
            sw.Stop();
            MetricsRegistry.WebhookDeliveryAttempts.Add(1,
                new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("project_id", delivery.ProjectId));
            MetricsRegistry.WebhookDeliveryDurationSeconds.Record(sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private static object BuildPayload(BackgroundResponseJob job) => new
    {
        jobId = job.JobId,
        workflowId = job.WorkflowId,
        executionId = job.ExecutionId,
        status = job.Status.ToString(),
        output = job.Status == BackgroundResponseStatus.Completed ? job.Output : null,
        lastError = job.Status == BackgroundResponseStatus.Failed ? job.LastError : null,
        completedAt = job.CompletedAt,
        attempt = job.Attempt,
    };

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
