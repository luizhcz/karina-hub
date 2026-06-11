using System.IO.Compression;
using System.Text.Json;
using Azure;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Hashing;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Implementação canônica do <see cref="IDocumentIntelligenceExtractor"/>.
/// Single-table writes em <c>aihub.document_extraction_jobs</c> (schema legacy intocável).
///
/// Otimizações pós-perf-review (2026-06):
/// <list type="bullet">
///   <item>Eventos agregados em buffer e gravados em 1 INSERT batch via
///         <see cref="IDocumentExtractionRepository.InsertEventsBatchAsync"/>.
///         11 round-trips → 1.</item>
///   <item>Gate de concorrência migrado pra <see cref="IDistributedSlotCounter"/>
///         (cross-pod), substituindo SemaphoreSlim local. <c>TryAcquireAsync</c>
///         é fail-fast (sem WaitAsync com timeout) — caller decide retry.</item>
///   <item>HttpClient nomeado com <c>Timeout=30s</c> via factory (antes default 100s).</item>
///   <item>Redis: <c>:full</c>/<c>:content</c>/<c>:meta</c> SETs em paralelo via
///         <c>Task.WhenAll</c>. Multiplexer envia em pipeline.</item>
///   <item>Gzip: <c>CompressionLevel.Fastest</c> ao invés de Optimal — 5x mais
///         rápido com ~15% menor ratio. Audit não precisa do menor tamanho.</item>
/// </list>
/// </summary>
public sealed class DocumentIntelligenceExtractor : IDocumentIntelligenceExtractor
{
    /// <summary>Nome do scope no contador de slots distribuído. Compartilhado com qualquer endpoint cross-pod que queira observar.</summary>
    public const string SlotScope = "document-intelligence";

    /// <summary>Nome do HttpClient com timeout custom configurado pra download de PDFs.</summary>
    public const string DownloadHttpClientName = "document-intelligence-download";

    private const string DiProvider = "AZUREAI";

    private static readonly Dictionary<string, decimal> FallbackPricePerPage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prebuilt-read"] = 0.0015m,
        ["prebuilt-layout"] = 0.01m,
        ["prebuilt-invoice"] = 0.01m,
        ["prebuilt-receipt"] = 0.01m,
        ["prebuilt-idDocument"] = 0.01m,
    };

    private readonly IDocumentExtractionRepository _repo;
    private readonly DocumentIntelligenceService _diService;
    private readonly IEfsRedisCache _redis;
    private readonly IDocumentIntelligencePricingCache _pricingCache;
    private readonly IDistributedSlotCounter _slots;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<DocumentIntelligenceExtractor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _slotTtl;

    public DocumentIntelligenceExtractor(
        IDocumentExtractionRepository repo,
        DocumentIntelligenceService diService,
        IEfsRedisCache redis,
        IDocumentIntelligencePricingCache pricingCache,
        IDistributedSlotCounter slots,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceExtractor> logger,
        IHttpClientFactory httpClientFactory)
    {
        _repo = repo;
        _diService = diService;
        _redis = redis;
        _pricingCache = pricingCache;
        _slots = slots;
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        // TTL do slot distribuído: cobre o pior caso de extração + buffer pra
        // pod crash não deixar slot perdido pra sempre. Math.Max evita
        // configuração patológica (CacheTtlDays=0 zeraria o slot na hora).
        var pollingTtl = Math.Max(60, _options.PollingTimeoutSeconds);
        _slotTtl = TimeSpan.FromSeconds(pollingTtl + 60);
    }

    public async Task<bool> HasCapacityAsync(CancellationToken ct)
    {
        // Advisory: GetActiveCount poda slots vencidos e devolve a contagem real
        // (ZCARD). Sem reserva — o teto é imposto atomicamente no TryAcquireAsync
        // dentro de ExtractAsync. Math.Max espelha o piso aplicado lá.
        var active = await _slots.GetActiveCountAsync(SlotScope);
        return active < Math.Max(1, _options.MaxConcurrentExtractions);
    }

    public async Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct)
    {
        var outputFormat = input.OutputFormat?.Equals("text", StringComparison.OrdinalIgnoreCase) == true
            ? "text"
            : "markdown";

        var jobId = Guid.NewGuid();
        var job = new ExtractionJob
        {
            Id = jobId,
            ConversationId = input.ConversationId,
            UserId = input.UserId,
            Model = input.Model,
            Status = "created",
        };

        // Buffer de eventos: tudo é acumulado in-memory e gravado em 1 INSERT
        // batch no finally externo. Antes: 11 round-trips. Agora: 1.
        // Capacity 14 cobre happy path + cache hit + failed sem realloc.
        var pendingEvents = new List<ExtractionEvent>(14);

        try
        {
            byte[] pdfBytes;
            Uri? sourceUri;
            switch (input.Source)
            {
                case ExtractionSource.Bytes b:
                    pdfBytes = b.Content;
                    sourceUri = null;
                    job.SourceType = b.SourceRef is not null ? "blobUrl" : "bytes";
                    job.SourceRef = b.SourceRef?.ToString();
                    break;

                case ExtractionSource.Url u:
                    if (u.Address is null)
                    {
                        return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.SourceUnavailable,
                            "ExtractionSource.Url.Address não pode ser null.", detail: null);
                    }
                    sourceUri = u.Address;
                    job.SourceType = "blobUrl";
                    job.SourceRef = u.Address.ToString();
                    try
                    {
                        // Named client com Timeout=30s configurado em DI. Default
                        // 100s seguraria worker em URL hostil — fix do perf review.
                        var httpClient = _httpClientFactory.CreateClient(DownloadHttpClientName);
                        pdfBytes = await httpClient.GetByteArrayAsync(u.Address, ct);
                    }
                    catch (HttpRequestException ex)
                    {
                        return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.SourceUnavailable,
                            $"Falha ao baixar PDF: {ex.Message}", detail: null);
                    }
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout do HttpClient (não do caller). Bate em SourceUnavailable.
                        return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.SourceUnavailable,
                            "Timeout ao baixar PDF (HTTP).", detail: null);
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"ExtractionSource não suportado: {input.Source.GetType().FullName}. " +
                        "Tipos válidos: ExtractionSource.Bytes / ExtractionSource.Url.");
            }

            var sha256 = ContentHashCalculator.ComputeFromBytes(pdfBytes);
            job.ContentSha256 = sha256;

            var featuresHash = ContentHashCalculator.ComputeFromString(
                (input.Features is { Length: > 0 }
                    ? string.Join(",", input.Features.OrderBy(f => f))
                    : "none") + "|fmt=" + outputFormat);
            job.FeaturesHash = featuresHash;

            pendingEvents.Add(new ExtractionEvent(jobId, "source_validated",
                JsonSerializer.Serialize(new { sha256, sourceType = job.SourceType }, JsonDefaults.Domain)));

            if (pdfBytes.Length > _options.MaxFileSizeBytes)
            {
                return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.UnreadablePdf,
                    $"Arquivo possui {pdfBytes.Length / (1024 * 1024.0):F1} MB. Limite máximo é {_options.MaxFileSizeBytes / (1024 * 1024.0):F0} MB.",
                    new { actualSizeBytes = pdfBytes.Length, maxAllowedBytes = _options.MaxFileSizeBytes });
            }

            if (pdfBytes.Length < 5 || pdfBytes[0] != 0x25 || pdfBytes[1] != 0x50 || pdfBytes[2] != 0x44 || pdfBytes[3] != 0x46)
            {
                return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.UnreadablePdf,
                    "Arquivo não é um PDF válido (magic bytes ausentes).", detail: null);
            }

            pendingEvents.Add(new ExtractionEvent(jobId, "file_validated",
                JsonSerializer.Serialize(new { sizeBytes = pdfBytes.Length, maxSizeBytes = _options.MaxFileSizeBytes }, JsonDefaults.Domain)));

            // Cache check.
            if (input.CacheEnabled)
            {
                var cached = await _repo.LookupCacheAsync(sha256, input.Model, featuresHash, ct);
                if (cached != null)
                {
                    // ExistsAsync foi removido — GetStringAsync já cobre o caso (null = miss).
                    // Economiza 1 round-trip por cache HIT path.
                    var cachedContent = await _redis.GetStringAsync(cached.ResultRef + ":content");
                    if (cachedContent is not null)
                    {
                        job.Status = "cached";
                        job.ResultRef = cached.ResultRef;
                        job.PageCount = cached.PageCount;
                        job.CostUsd = 0m;
                        job.FinishedAt = DateTime.UtcNow;
                        await _repo.UpsertJobAsync(job, ct);
                        pendingEvents.Add(new ExtractionEvent(jobId, "cache_hit",
                            JsonSerializer.Serialize(new { resultRef = cached.ResultRef }, JsonDefaults.Domain)));

                        _logger.LogInformation("[DocIntel] Cache HIT hash='{Hash}' model='{Model}'.", sha256[..12], input.Model);

                        return new ExtractionResult(
                            JobId: jobId,
                            Status: "cached",
                            Content: cachedContent,
                            ResultRef: cached.ResultRef,
                            PageCount: cached.PageCount,
                            CostUsd: 0m,
                            FromCache: true,
                            OperationId: null,
                            DurationMs: null,
                            ErrorCode: null,
                            ErrorMessage: null,
                            ErrorDetail: null);
                    }
                }
                pendingEvents.Add(new ExtractionEvent(jobId, "cache_miss"));
            }

            job.Status = "running";
            job.StartedAt = DateTime.UtcNow;
            // Persistência da linha de auditoria movida pra DEPOIS do gate: sem vaga
            // nada roda, então não deve sobrar registro algum (ver bloco do gate).

            // Gate distribuído (cross-pod). TryAcquire é fail-fast — sem WaitAsync
            // com timeout. Quando saturado, retorna GateTimeout imediatamente e
            // caller decide: ingestion devolve job pra Queued com backoff; agent
            // tool propaga pro workflow tratar.
            pendingEvents.Add(new ExtractionEvent(jobId, "gate_waiting"));

            var maxConcurrent = Math.Max(1, _options.MaxConcurrentExtractions);
            bool slotAcquired = false;
            try
            {
                slotAcquired = await _slots.TryAcquireAsync(SlotScope, maxConcurrent, _slotTtl);
                if (!slotAcquired)
                {
                    // Backpressure de capacidade NÃO é uma extração — nada rodou.
                    // NÃO persiste linha de auditoria nem eventos: senão cada espera
                    // deixaria um 'failed/GATE_TIMEOUT' órfão que a tentativa
                    // bem-sucedida nunca reconcilia (cada ExtractAsync usa um id novo).
                    // O caller (ingestion) defere; só quem pega vaga vira registro.
                    pendingEvents.Clear();
                    return GateTimeoutResult(jobId, maxConcurrent);
                }

                pendingEvents.Add(new ExtractionEvent(jobId, "gate_acquired"));
                // Vaga garantida: só agora persiste a linha "running" da extração.
                await _repo.UpsertJobAsync(job, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PollingTimeoutSeconds));

                pendingEvents.Add(new ExtractionEvent(jobId, "di_submitted"));

                var diResult = sourceUri is not null
                    ? await _diService.AnalyzeAsync(sourceUri, input.Model, input.Features, outputFormat, timeoutCts.Token)
                    : await _diService.AnalyzeBytesAsync(pdfBytes, input.Model, input.Features, outputFormat, timeoutCts.Token);

                job.OperationId = diResult.OperationId;
                pendingEvents.Add(new ExtractionEvent(jobId, "di_succeeded",
                    JsonSerializer.Serialize(new { operationId = diResult.OperationId, pages = diResult.PageCount, durationMs = diResult.DurationMs }, JsonDefaults.Domain)));

                var resultRef = $"di:v2:{sha256}:{input.Model}:{outputFormat}";
                var fullKey = _redis.BuildKey(resultRef + ":full");
                var gzipped = GzipCompress(diResult.RawJson);
                var ttl = TimeSpan.FromDays(_options.CacheTtlDays);

                // 3 SETs em paralelo: StackExchange.Redis multiplexer envia em pipeline
                // transparente, ganho real ~9ms em Redis remoto. Antes era serial.
                var fullTask = _redis.Database.StringSetAsync(fullKey, gzipped, ttl);
                var contentTask = _redis.SetStringAsync(resultRef + ":content", diResult.Content, ttl);
                var metaTask = _redis.SetStringAsync(resultRef + ":meta", JsonSerializer.Serialize(new
                {
                    pageCount = diResult.PageCount,
                    hasTables = diResult.HasTables,
                    hasHandwriting = diResult.HasHandwriting,
                    primaryLanguage = diResult.PrimaryLanguage,
                }, JsonDefaults.Domain), ttl);
                await Task.WhenAll(fullTask, contentTask, metaTask);

                pendingEvents.Add(new ExtractionEvent(jobId, "pages_stored",
                    JsonSerializer.Serialize(new { resultRef }, JsonDefaults.Domain)));

                job.PageCount = diResult.PageCount;
                await _repo.UpsertCacheAsync(new ExtractionCacheEntry(
                    sha256, input.Model, featuresHash, resultRef, diResult.PageCount, DateTime.UtcNow.Add(ttl)), ct);

                var costUsd = await ResolveCostAsync(input.Model, diResult.PageCount, ct);

                job.CostUsd = costUsd;
                job.ResultRef = resultRef;
                job.Status = "succeeded";
                job.FinishedAt = DateTime.UtcNow;
                job.DurationMs = (int)(job.FinishedAt.Value - job.StartedAt.Value).TotalMilliseconds;
                // CancellationToken.None na finalização: Azure DI já cobrou e Redis já tem
                // o resultado — abortar entre `di_succeeded` e `completed` deixaria o job
                // em status="running" órfão.
                await _repo.UpsertJobAsync(job, CancellationToken.None);

                pendingEvents.Add(new ExtractionEvent(jobId, "completed",
                    JsonSerializer.Serialize(new { costUsd, durationMs = job.DurationMs }, JsonDefaults.Domain)));

                _logger.LogInformation("[DocIntel] Job '{JobId}' concluído: {Pages} páginas, custo ${Cost}, {DurationMs}ms.",
                    jobId, diResult.PageCount, costUsd, job.DurationMs);

                return new ExtractionResult(
                    JobId: jobId,
                    Status: "succeeded",
                    Content: diResult.Content,
                    ResultRef: resultRef,
                    PageCount: diResult.PageCount,
                    CostUsd: costUsd,
                    FromCache: false,
                    OperationId: diResult.OperationId,
                    DurationMs: job.DurationMs,
                    ErrorCode: null,
                    ErrorMessage: null,
                    ErrorDetail: null);
            }
            finally
            {
                if (slotAcquired)
                {
                    try { await _slots.ReleaseAsync(SlotScope); }
                    catch (Exception ex) { _logger.LogDebug(ex, "[DocIntel] Falha ao liberar slot distribuído (TTL cobre)."); }
                    pendingEvents.Add(new ExtractionEvent(jobId, "gate_released"));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = "failed";
            job.ErrorCode = ExtractionErrorCode.Cancelled;
            job.ErrorMessage = "Operação cancelada pelo workflow.";
            job.FinishedAt = DateTime.UtcNow;
            await PersistJobBestEffortAsync(job);
            pendingEvents.Add(new ExtractionEvent(job.Id, "failed",
                JsonSerializer.Serialize(new { errorCode = job.ErrorCode, errorMessage = job.ErrorMessage }, JsonDefaults.Domain)));
            throw;
        }
        catch (OperationCanceledException)
        {
            return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.Timeout,
                $"Timeout após {_options.PollingTimeoutSeconds}s.", detail: null);
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            // Azure DI 400: PDF inválido OU misconfig — sem coluna nova pra distinguir.
            // Tudo vira AzureDiFailure; detalhe semântico no error_message via azureErrorCode.
            return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.AzureDiFailure,
                $"Azure DI 400 (errorCode={ex.ErrorCode}): {ex.Message}",
                new { azureErrorCode = ex.ErrorCode, azureStatus = ex.Status });
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            job.Status = "failed";
            job.ErrorCode = ExtractionErrorCode.AzureDiFailure;
            job.ErrorMessage = "Falha de autenticação com Azure DI.";
            job.FinishedAt = DateTime.UtcNow;
            await PersistJobBestEffortAsync(job);
            pendingEvents.Add(new ExtractionEvent(job.Id, "failed",
                JsonSerializer.Serialize(new { errorCode = job.ErrorCode, errorMessage = job.ErrorMessage }, JsonDefaults.Domain)));
            throw;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.AzureDiFailure,
                "Rate limit atingido no Azure DI.", detail: null);
        }
        catch (RequestFailedException ex)
        {
            return await FinalizeFailedAsync(job, pendingEvents, ExtractionErrorCode.AzureDiFailure,
                $"Status {ex.Status}: {ex.Message}", detail: null);
        }
        finally
        {
            // Flush batch dos eventos: 1 INSERT contendo todos os ~11 eventos do job.
            // Roda mesmo em paths de exception (catch propaga, finally executa antes).
            // CancellationToken.None: persiste audit mesmo se caller cancelar.
            if (pendingEvents.Count > 0)
            {
                try { await _repo.InsertEventsBatchAsync(pendingEvents, CancellationToken.None); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[DocIntel] Falha ao gravar batch de {Count} eventos do job '{JobId}'.",
                        pendingEvents.Count, jobId);
                }
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // Resultado de gate cheio SEM efeito colateral de persistência: capacidade é
    // espera, não falha de extração — não grava em document_extraction_jobs/events.
    // O caller reconhece GATE_TIMEOUT (IsCapacityBackpressure) e re-enfileira; a
    // tentativa que conseguir vaga é a única que vira registro de auditoria.
    private static ExtractionResult GateTimeoutResult(Guid jobId, int maxConcurrent) => new(
        JobId: jobId,
        Status: "failed",
        Content: null,
        ResultRef: null,
        PageCount: 0,
        CostUsd: 0m,
        FromCache: false,
        OperationId: null,
        DurationMs: null,
        ErrorCode: ExtractionErrorCode.GateTimeout,
        ErrorMessage: $"Capacidade global '{SlotScope}' esgotada ({maxConcurrent} slots). Aguardando vaga.",
        ErrorDetail: null);

    private async Task<ExtractionResult> FinalizeFailedAsync(
        ExtractionJob job, List<ExtractionEvent> pendingEvents,
        string errorCode, string errorMessage, object? detail)
    {
        job.Status = "failed";
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.FinishedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
            job.DurationMs = (int)(job.FinishedAt.Value - job.StartedAt.Value).TotalMilliseconds;

        await PersistJobBestEffortAsync(job);
        pendingEvents.Add(new ExtractionEvent(job.Id, "failed",
            JsonSerializer.Serialize(new { errorCode, errorMessage }, JsonDefaults.Domain)));

        _logger.LogWarning("[DocIntel] Job '{JobId}' falhou: {ErrorCode} — {ErrorMessage}.", job.Id, errorCode, errorMessage);

        return new ExtractionResult(
            JobId: job.Id,
            Status: "failed",
            Content: null,
            ResultRef: null,
            PageCount: job.PageCount ?? 0,
            CostUsd: job.CostUsd ?? 0m,
            FromCache: false,
            OperationId: job.OperationId,
            DurationMs: job.DurationMs,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            ErrorDetail: detail);
    }

    private async Task PersistJobBestEffortAsync(ExtractionJob job)
    {
        try
        {
            await _repo.UpsertJobAsync(job, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocIntel] Falha ao persistir job '{JobId}' (best-effort).", job.Id);
        }
    }

    private async Task<decimal> ResolveCostAsync(string model, int pages, CancellationToken ct)
    {
        if (pages <= 0) return 0m;

        var pricing = await _pricingCache.GetAsync(model, DiProvider, ct);
        if (pricing is not null)
            return pricing.PricePerPage * pages;

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
        // CompressionLevel.Fastest: ~5x mais rápido que Optimal, ~15% menor ratio.
        // Audit não precisa do menor tamanho — CPU sob gate de concorrência alto
        // (PDFs grandes paralelos) era o gargalo. Perf review do refactor 2026-06.
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
