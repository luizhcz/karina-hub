using System.IO.Compression;
using System.Text.Json;
using Azure;
using EfsAiHub.Core.Abstractions.Hashing;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
namespace EfsAiHub.Platform.Runtime.Executors;

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

    // Fallback hardcoded caso o DB de pricing esteja vazio (novo ambiente,
    // seed não aplicado). Valores batem com seed_document_intelligence_pricing.sql.
    // Usado para não quebrar extração em ambientes sem seed.
    private static readonly Dictionary<string, decimal> FallbackPricePerPage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prebuilt-read"] = 0.0015m,
        ["prebuilt-layout"] = 0.01m,
        ["prebuilt-invoice"] = 0.01m,
        ["prebuilt-receipt"] = 0.01m,
        ["prebuilt-idDocument"] = 0.01m,
    };

    private const string DiProvider = "AZUREAI";

    private readonly IDocumentExtractionRepository _repo;
    private readonly IDocumentIntelligenceService _diService;
    private readonly IEfsRedisCache _redis;
    private readonly IDocumentIntelligencePricingCache _pricingCache;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<DocumentIntelligenceFunctions> _logger;
    private readonly HttpClient _httpClient;

    public DocumentIntelligenceFunctions(
        IDocumentExtractionRepository repo,
        IDocumentIntelligenceService diService,
        IEfsRedisCache redis,
        IDocumentIntelligencePricingCache pricingCache,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceFunctions> logger,
        IHttpClientFactory httpClientFactory)
    {
        _repo = repo;
        _diService = diService;
        _redis = redis;
        _pricingCache = pricingCache;
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<string> ExecuteAsync(string input, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<ExtractionRequest>(input, JsonDefaults.CaseInsensitive)
            ?? throw new InvalidOperationException("Input JSON inválido para ExtractionRequest.");

        // Normaliza format: aceita qualquer case, default markdown.
        var outputFormat = request.OutputFormat?.Equals("text", StringComparison.OrdinalIgnoreCase) == true
            ? "text"
            : "markdown";

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
            Uri? sourceUri = null;
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
            }
            else
            {
                return BuildErrorOutput(job, ExtractionErrorCode.SourceUnavailable, $"Tipo de source '{request.Source.Type}' não suportado.");
            }

            // 2. SHA-256
            var sha256 = ContentHashCalculator.ComputeFromBytes(pdfBytes);
            job.ContentSha256 = sha256;

            // features_hash incorpora outputFormat: muda de markdown pra text
            // invalida cache corretamente (conteúdo realmente diferente).
            var featuresHash = ContentHashCalculator.ComputeFromString(
                (request.Features is { Length: > 0 }
                    ? string.Join(",", request.Features.OrderBy(f => f))
                    : "none") + "|fmt=" + outputFormat);
            job.FeaturesHash = featuresHash;

            await _repo.InsertEventAsync(new ExtractionEvent(jobId, "source_validated", JsonSerializer.Serialize(new { sha256, sourceType = job.SourceType })), ct);

            // 3. Validate file size (leve, sem dependências externas)
            if (pdfBytes.Length > _options.MaxFileSizeBytes)
            {
                return BuildErrorOutput(job, ExtractionErrorCode.FileSizeExceeded,
                    $"Arquivo possui {pdfBytes.Length / (1024 * 1024.0):F1} MB. Limite máximo é {_options.MaxFileSizeBytes / (1024 * 1024.0):F0} MB.",
                    new { actualSizeBytes = pdfBytes.Length, maxAllowedBytes = _options.MaxFileSizeBytes });
            }

            // Validate PDF magic bytes
            if (pdfBytes.Length < 5 || pdfBytes[0] != 0x25 || pdfBytes[1] != 0x50 || pdfBytes[2] != 0x44 || pdfBytes[3] != 0x46)
            {
                return BuildErrorOutput(job, ExtractionErrorCode.UnreadablePdf, "Arquivo não é um PDF válido (magic bytes ausentes).");
            }

            await _repo.InsertEventAsync(new ExtractionEvent(jobId, "file_validated",
                JsonSerializer.Serialize(new { sizeBytes = pdfBytes.Length, maxSizeBytes = _options.MaxFileSizeBytes })), ct);

            // 4. Cache check
            if (request.CacheEnabled)
            {
                var cached = await _repo.LookupCacheAsync(sha256, request.Model, featuresHash, ct);
                if (cached != null)
                {
                    var redisKey = cached.ResultRef + ":full";
                    if (await _redis.ExistsAsync(redisKey))
                    {
                        var cachedContent = await _redis.GetStringAsync(cached.ResultRef + ":content");

                        job.Status = "cached";
                        job.ResultRef = cached.ResultRef;
                        job.PageCount = cached.PageCount;
                        job.CostUsd = 0m;
                        job.FinishedAt = DateTime.UtcNow;
                        await _repo.InsertJobAsync(job, ct);
                        await _repo.InsertEventAsync(new ExtractionEvent(jobId, "cache_hit", JsonSerializer.Serialize(new { resultRef = cached.ResultRef })), ct);

                        _logger.LogInformation("[DocIntel] Cache HIT para hash '{Hash}', model '{Model}'.", sha256[..12], request.Model);
                        return BuildSuccessOutput(job, fromCache: true, content: cachedContent);
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

                var result = sourceUri is not null
                    ? await _diService.AnalyzeAsync(sourceUri, request.Model, request.Features, outputFormat, timeoutCts.Token)
                    : await _diService.AnalyzeBytesAsync(pdfBytes, request.Model, request.Features, outputFormat, timeoutCts.Token);

                job.OperationId = result.OperationId;
                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "di_succeeded",
                    JsonSerializer.Serialize(new { operationId = result.OperationId, pages = result.PageCount, durationMs = result.DurationMs })), ct);

                // 7. Store in Redis (gzip full + meta)
                // v2 key inclui outputFormat — cache antigo v1 fica órfão (TTL limpa).
                var resultRef = $"di:v2:{sha256}:{request.Model}:{outputFormat}";
                var fullKey = _redis.BuildKey(resultRef + ":full");
                var gzipped = GzipCompress(result.RawJson);
                var ttl = TimeSpan.FromDays(_options.CacheTtlDays);

                await _redis.Database.StringSetAsync(fullKey, gzipped, ttl);
                await _redis.SetStringAsync(resultRef + ":content", result.Content, ttl);
                await _redis.SetStringAsync(resultRef + ":meta", JsonSerializer.Serialize(new
                {
                    pageCount = result.PageCount,
                    hasTables = result.HasTables,
                    hasHandwriting = result.HasHandwriting,
                    primaryLanguage = result.PrimaryLanguage,
                }), ttl);

                await _repo.InsertEventAsync(new ExtractionEvent(jobId, "pages_stored", JsonSerializer.Serialize(new { resultRef })), ct);

                // 8. Upsert cache
                job.PageCount = result.PageCount;
                await _repo.UpsertCacheAsync(new ExtractionCacheEntry(
                    sha256, request.Model, featuresHash, resultRef, result.PageCount, DateTime.UtcNow.Add(ttl)), ct);

                // 9. Cost + budget integration — preço vem do DB (cache in-memory → Redis → PG).
                // Se tabela document_intelligence_pricing estiver vazia, cai no fallback hardcoded.
                var costUsd = await ResolveCostAsync(request.Model, result.PageCount, ct);
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
                    jobId, result.PageCount, costUsd, job.DurationMs);

                return BuildSuccessOutput(job, fromCache: false, content: result.Content);
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

    private string BuildSuccessOutput(ExtractionJob job, bool fromCache, string? content = null)
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
            content,
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

    // Resolve o preço por página via cache (→ Redis → PG). Fallback para valores
    // hardcoded se o DB estiver vazio. Garante que extração nunca falha por
    // ausência de seed de pricing.
    private async Task<decimal> ResolveCostAsync(string model, int pages, CancellationToken ct)
    {
        if (pages <= 0) return 0m;

        var pricing = await _pricingCache.GetAsync(model, DiProvider, ct);
        if (pricing is not null)
            return pricing.PricePerPage * pages;

        // Fallback — loga warning pra visibilidade; valor bate com o seed oficial.
        var fallback = FallbackPricePerPage.TryGetValue(model, out var price) ? price : 0.01m;
        _logger.LogWarning(
            "[DocIntel] Pricing não encontrado no DB para '{Model}' (provider={Provider}). " +
            "Usando fallback hardcoded ${Fallback}/pág. Rode seed_document_intelligence_pricing.sql.",
            model, DiProvider, fallback);
        return fallback * pages;
    }

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
