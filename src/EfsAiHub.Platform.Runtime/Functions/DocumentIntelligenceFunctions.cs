using System.IO.Compression;
using System.Text.Json;
using Azure;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Options;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Abstractions.Hashing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using IronPdf;

namespace EfsAiHub.Platform.Runtime.Functions;

/// <summary>
/// Executor que usa Azure Document Intelligence para extrair dados estruturados de PDFs.
/// Registrado como "document_intelligence" no CodeExecutorRegistry.
///
/// Reutiliza: ContentHashCalculator (hash), JsonDefaults (serialização),
/// ExecutionBudget.AddCost (custo), IEfsRedisCache (storage).
/// </summary>
public class DocumentIntelligenceFunctions
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static int _queueDepth;

    private readonly IDocumentExtractionRepository _repo;
    private readonly IDocumentIntelligenceService _diService;
    private readonly IEfsRedisCache _redis;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<DocumentIntelligenceFunctions> _logger;
    private readonly HttpClient _httpClient;

    public DocumentIntelligenceFunctions(
        IDocumentExtractionRepository repo,
        IDocumentIntelligenceService diService,
        IEfsRedisCache redis,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceFunctions> logger,
        IHttpClientFactory httpClientFactory)
    {
        _repo = repo;
        _diService = diService;
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<string> ExecuteAsync(string input, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<ExtractionRequest>(input, JsonDefaults.CaseInsensitive)
            ?? throw new InvalidOperationException("Input JSON inválido para ExtractionRequest.");

        var ctx = DelegateExecutor.Current.Value;
        var jobId = Guid.NewGuid();
        var conversationId = ctx?.ConversationId ?? "";
        var userId = ctx?.UserId ?? "";

        var job = new ExtractionJob
        {
            Id = jobId,
            ConversationId = conversationId,
            UserId = userId,
            Model = request.Model,
            Status = "created",
        };

        try
        {
            // 1. Resolve source → bytes
            byte[] pdfBytes;
            Uri sourceUri;
            if (string.Equals(request.Source.Type, "blobUrl", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.Source.Url))
                    return BuildErrorOutput(job, ExtractionErrorCode.SourceUnavailable, "URL não fornecida.");

                sourceUri = new Uri(request.Source.Url);
                job.SourceType = "blobUrl";
                job.SourceRef = request.Source.Url;

                try
                {
                    pdfBytes = await _httpClient.GetByteArrayAsync(sourceUri, ct);
                }
                catch (HttpRequestException ex)
                {
                    return BuildErrorOutput(job, ExtractionErrorCode.SourceUnavailable, $"Falha ao baixar PDF: {ex.Message}");
                }
            }
            else if (string.Equals(request.Source.Type, "bytes", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.Source.Bytes))
                    return BuildErrorOutput(job, ExtractionErrorCode.SourceUnavailable, "Bytes não fornecidos.");

                try
                {
                    pdfBytes = Convert.FromBase64String(request.Source.Bytes);
                }
                catch (FormatException)
                {
                    return BuildErrorOutput(job, ExtractionErrorCode.UnreadablePdf, "Base64 inválido.");
                }

                job.SourceType = "bytes";
                sourceUri = new Uri("data:application/pdf;base64,placeholder");
            }
            else
            {
                return BuildErrorOutput(job, ExtractionErrorCode.SourceUnavailable, $"Tipo de source '{request.Source.Type}' não suportado.");
            }

            // 2. SHA-256
            var sha256 = ContentHashCalculator.ComputeFromBytes(pdfBytes);
            job.ContentSha256 = sha256;

            var featuresHash = request.Features is { Length: > 0 }
                ? ContentHashCalculator.ComputeFromString(string.Join(",", request.Features.OrderBy(f => f)))
                : "none";
            job.FeaturesHash = featuresHash;

            await _repo.InsertEventAsync(new ExtractionEvent(jobId, "source_validated", JsonSerializer.Serialize(new { sha256, sourceType = job.SourceType })), ct);

            // 3. Count pages (IronPdf — local, barato)
            int pageCount;
            try
            {
                using var pdfDoc = new PdfDocument(pdfBytes);
                pageCount = pdfDoc.PageCount;
            }
            catch (Exception ex)
            {
                return BuildErrorOutput(job, ExtractionErrorCode.UnreadablePdf, $"PDF corrompido ou protegido: {ex.Message}");
            }

            job.PageCount = pageCount;
            await _repo.InsertEventAsync(new ExtractionEvent(jobId, "page_count_validated",
                JsonSerializer.Serialize(new { pageCount, limit = _options.MaxPages, passed = pageCount <= _options.MaxPages })), ct);

            if (pageCount > _options.MaxPages)
            {
                return BuildErrorOutput(job, ExtractionErrorCode.PageLimitExceeded,
                    $"Documento possui {pageCount} páginas. Limite máximo é {_options.MaxPages}.",
                    new { actualPageCount = pageCount, maxAllowedPages = _options.MaxPages });
            }

            // 4. Cache check
            if (request.CacheEnabled)
            {
                var cached = await _repo.LookupCacheAsync(sha256, request.Model, featuresHash, ct);
                if (cached != null)
                {
                    var redisKey = cached.ResultRef + ":full";
                    if (await _redis.ExistsAsync(redisKey))
                    {
                        job.Status = "cached";
                        job.ResultRef = cached.ResultRef;
                        job.CostUsd = 0m;
                        job.FinishedAt = DateTime.UtcNow;
                        await _repo.InsertJobAsync(job, ct);
                        await _repo.InsertEventAsync(new ExtractionEvent(jobId, "cache_hit", JsonSerializer.Serialize(new { resultRef = cached.ResultRef })), ct);

                        _logger.LogInformation("[DocIntel] Cache HIT para hash '{Hash}', model '{Model}'.", sha256[..12], request.Model);
                        return BuildSuccessOutput(job, fromCache: true);
                    }
                }
                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "cache_miss"), ct);
            }

            // 5. Insert job + acquire gate
            job.Status = "running";
            job.StartedAt = DateTime.UtcNow;
            await _repo.InsertJobAsync(job, ct);

            Interlocked.Increment(ref _queueDepth);
            await _repo.InsertEventAsync(new ExtractionEvent(jobId, "gate_waiting", JsonSerializer.Serialize(new { queueDepth = _queueDepth })), ct);

            if (!await _gate.WaitAsync(TimeSpan.FromSeconds(_options.GateWaitTimeoutSeconds), ct))
            {
                Interlocked.Decrement(ref _queueDepth);
                return BuildErrorOutput(job, ExtractionErrorCode.GateTimeout, "Sistema ocupado. Tente novamente em alguns instantes.");
            }
            Interlocked.Decrement(ref _queueDepth);

            try
            {
                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "gate_acquired"), ct);

                // 6. Call Azure DI with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PollingTimeoutSeconds));

                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "di_submitted"), ct);

                var result = await _diService.AnalyzeAsync(sourceUri, request.Model, request.Features, timeoutCts.Token);

                job.OperationId = result.OperationId;
                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "di_succeeded",
                    JsonSerializer.Serialize(new { operationId = result.OperationId, pages = result.PageCount, durationMs = result.DurationMs })), ct);

                // 7. Store in Redis (gzip full + meta)
                var resultRef = $"di:v1:{sha256}:{request.Model}";
                var fullKey = _redis.BuildKey(resultRef + ":full");
                var gzipped = GzipCompress(result.RawJson);
                var ttl = TimeSpan.FromDays(_options.CacheTtlDays);

                await _redis.Database.StringSetAsync(fullKey, gzipped, ttl);
                await _redis.SetStringAsync(resultRef + ":meta", JsonSerializer.Serialize(new
                {
                    pageCount = result.PageCount,
                    hasTables = result.HasTables,
                    hasHandwriting = result.HasHandwriting,
                    primaryLanguage = result.PrimaryLanguage,
                }), ttl);

                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "pages_stored", JsonSerializer.Serialize(new { resultRef })), ct);

                // 8. Upsert cache
                await _repo.UpsertCacheAsync(new ExtractionCacheEntry(
                    sha256, request.Model, featuresHash, resultRef, pageCount, DateTime.UtcNow.Add(ttl)), ct);

                // 9. Cost + budget integration
                var costUsd = EstimateCost(request.Model, pageCount);
                ctx?.Budget.AddCost(costUsd);

                job.CostUsd = costUsd;
                job.ResultRef = resultRef;
                job.Status = "succeeded";
                job.FinishedAt = DateTime.UtcNow;
                job.DurationMs = (int)(job.FinishedAt.Value - job.StartedAt.Value).TotalMilliseconds;
                await _repo.UpdateJobAsync(job, ct);

                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "completed",
                    JsonSerializer.Serialize(new { costUsd, durationMs = job.DurationMs })), ct);

                _logger.LogInformation("[DocIntel] Job '{JobId}' concluído: {Pages} páginas, custo ${Cost}, {DurationMs}ms.",
                    jobId, pageCount, costUsd, job.DurationMs);

                return BuildSuccessOutput(job, fromCache: false);
            }
            finally
            {
                _gate.Release();
                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "gate_released"), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await FailJobAsync(job, ExtractionErrorCode.Cancelled, "Operação cancelada pelo workflow.");
            throw;
        }
        catch (OperationCanceledException)
        {
            return await FailJobAsync(job, ExtractionErrorCode.Timeout,
                $"Timeout após {_options.PollingTimeoutSeconds}s.");
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            return await FailJobAsync(job, ExtractionErrorCode.UnreadablePdf, ex.Message);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            await FailJobAsync(job, ExtractionErrorCode.AzureDiFailure, "Falha de autenticação com Azure DI.");
            throw;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            return await FailJobAsync(job, ExtractionErrorCode.AzureDiFailure, "Rate limit atingido no Azure DI.");
        }
        catch (RequestFailedException ex)
        {
            return await FailJobAsync(job, ExtractionErrorCode.AzureDiFailure, $"Status {ex.Status}: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private string BuildSuccessOutput(ExtractionJob job, bool fromCache)
    {
        return JsonSerializer.Serialize(new
        {
            status = job.Status,
            jobId = job.Id.ToString(),
            resultRef = job.ResultRef,
            pageCount = job.PageCount,
            cached = fromCache,
            costUsd = job.CostUsd,
            model = job.Model,
            pageAccess = new
            {
                fullKey = $"{job.ResultRef}:full",
                metaKey = $"{job.ResultRef}:meta",
            },
        }, JsonDefaults.CaseInsensitive);
    }

    private string BuildErrorOutput(ExtractionJob job, string errorCode, string errorMessage, object? detail = null)
    {
        job.Status = "failed";
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.FinishedAt = DateTime.UtcNow;

        return JsonSerializer.Serialize(new
        {
            status = "failed",
            jobId = job.Id.ToString(),
            errorCode,
            errorMessage,
            detail,
        }, JsonDefaults.CaseInsensitive);
    }

    private async Task<string> FailJobAsync(ExtractionJob job, string errorCode, string errorMessage)
    {
        job.Status = "failed";
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.FinishedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
            job.DurationMs = (int)(job.FinishedAt.Value - job.StartedAt.Value).TotalMilliseconds;

        try { await _repo.UpdateJobAsync(job, CancellationToken.None); } catch { /* best-effort */ }
        await _repo.InsertEventAsync(new ExtractionEvent(job.Id, "failed",
            JsonSerializer.Serialize(new { errorCode, errorMessage })), CancellationToken.None);

        _logger.LogWarning("[DocIntel] Job '{JobId}' falhou: {ErrorCode} — {ErrorMessage}.", job.Id, errorCode, errorMessage);

        return JsonSerializer.Serialize(new
        {
            status = "failed",
            jobId = job.Id.ToString(),
            errorCode,
            errorMessage,
        }, JsonDefaults.CaseInsensitive);
    }

    private static decimal EstimateCost(string model, int pages) => model switch
    {
        "prebuilt-read"    => pages * 0.0015m,
        "prebuilt-layout"  => pages * 0.01m,
        "prebuilt-invoice" => pages * 0.01m,
        "prebuilt-receipt" => pages * 0.01m,
        _                  => pages * 0.01m,
    };

    private static byte[] GzipCompress(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
