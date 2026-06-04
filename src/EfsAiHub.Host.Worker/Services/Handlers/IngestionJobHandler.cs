using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Ingestion;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services.Handlers;

/// <summary>
/// Handler de jobs de ingestão URL→PDF/TXT/MD→workflow. Máquina de estados
/// persistida via <c>Step</c> + <c>IngestionContext</c>, idempotente por step:
/// crash do pod em qualquer ponto retoma de onde parou na próxima vez que o
/// reaper devolve o job pra Queued.
///
/// Bytes do PDF baixado ficam em Redis (chave <c>ingestion:pdf:{jobId}</c>)
/// com TTL ≥ <c>JobMaxLifetimeMinutes</c>, NÃO no IngestionContext JSONB —
/// pra que GET /responses não devolva payload de dezenas de MB ao cliente e o
/// Postgres não pague reescrita de row inteira a cada UpdateIngestionContext.
/// TXT/MD são decodificados pra UTF-8 já no download e armazenados em
/// <c>ExtractedContent</c> direto (sem trip extra pelo Redis).
///
/// Steps:
/// <list type="bullet">
///   <item><c>null</c>/<c>"Queued"</c> → baixa o arquivo via <see cref="IngestionDownloader"/>.</item>
///   <item><c>"Downloading"</c>/<c>"Validating"</c> → detecta tipo (PDF/TXT/MD) e
///         persiste resultado: PDF vai pro Redis cache; TXT/MD vão direto pro
///         <c>ExtractedContent</c>.</item>
///   <item><c>"Extracting"</c> (só PDF) → chama Document Intelligence
///         (<c>prebuilt-layout</c>, markdown). TXT/MD pulam direto pra
///         <c>"ContentPersisted"</c>.</item>
///   <item><c>"ContentPersisted"</c> → dispara workflow via <see cref="IWorkflowDispatcher"/>
///         com input contendo o conteúdo extraído + metadata do cliente.</item>
///   <item><c>"WorkflowRunning"</c> → polla <c>workflow_executions</c> até terminal.</item>
/// </list>
///
/// Tipos não suportados (qualquer coisa fora PDF/TXT/MD) → Status=Failed permanente.
/// </summary>
public sealed class IngestionJobHandler : IStandaloneJobHandler
{
    private const string StepDownloading = "Downloading";
    private const string StepValidating = "Validating";
    private const string StepExtracting = "Extracting";
    private const string StepContentPersisted = "ContentPersisted";
    private const string StepWorkflowRunning = "WorkflowRunning";

    private const string PdfRedisKeyPrefix = "ingestion:pdf:";

    private static readonly JsonSerializerOptions JsonOpts = IngestionJsonDefaults.Options;

    private readonly IngestionDownloader _downloader;
    private readonly IEfsRedisCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDocumentIntelligenceService _diService;
    private readonly DocumentIntelligenceOptions _diOptions;
    private readonly StandalonePoolsOptions _poolOptions;
    private readonly IngestionApiOptions _ingestionOptions;
    private readonly ILogger<IngestionJobHandler> _logger;

    public IngestionJobHandler(
        IngestionDownloader downloader,
        IEfsRedisCache cache,
        IServiceScopeFactory scopeFactory,
        IDocumentIntelligenceService diService,
        IOptions<DocumentIntelligenceOptions> diOptions,
        IOptions<StandalonePoolsOptions> poolOptions,
        IOptions<IngestionApiOptions> ingestionOptions,
        ILogger<IngestionJobHandler> logger)
    {
        _downloader = downloader;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _diService = diService;
        _diOptions = diOptions.Value;
        _poolOptions = poolOptions.Value;
        _ingestionOptions = ingestionOptions.Value;
        _logger = logger;
    }

    public bool CanHandle(BackgroundResponseJob job)
    {
        if (string.IsNullOrWhiteSpace(job.IngestionContext)) return false;
        var ctx = TryParseContext(job.IngestionContext);
        return ctx is not null && !string.IsNullOrWhiteSpace(ctx.Url);
    }

    public async Task ProcessAsync(BackgroundResponseJob job, IStandaloneJobContext ctx, CancellationToken ct)
    {
        if (!_ingestionOptions.Enabled)
        {
            await ctx.FailAsync("Pool de ingestão desabilitado (IngestionApi.Enabled=false).", null, permanent: true, ct)
                .ConfigureAwait(false);
            return;
        }

        var state = TryParseContext(job.IngestionContext)
            ?? throw new InvalidOperationException("IngestionContext inválido.");

        if (string.IsNullOrWhiteSpace(state.Url))
        {
            await ctx.FailAsync("IngestionContext.url obrigatório.", null, permanent: true, ct).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(job.WorkflowId))
        {
            await ctx.FailAsync("WorkflowId obrigatório no job de ingestão.", null, permanent: true, ct).ConfigureAwait(false);
            return;
        }

        var step = job.Step ?? string.Empty;

        // Retomada baseada no Step. Em fluxo "feliz" (sem crash) cada caso cai
        // pro próximo via labels; em retomada, salta pro step persistido.
        switch (step.ToLowerInvariant())
        {
            case "":
            case "queued":
            case "downloading":
                state = await DownloadAndDetectAsync(job, state, ctx, ct).ConfigureAwait(false);
                if (state is null) return;
                goto case "validating";

            case "validating":
                state = await ResolveContentAsync(job, state, ctx, ct).ConfigureAwait(false);
                if (state is null) return;
                goto case "contentpersisted";

            case "extracting":
                // Retomada durante extração — refaz desde detect (PDF bytes
                // talvez ainda estejam no Redis; senão re-baixa).
                state = await ResolveContentAsync(job, state, ctx, ct).ConfigureAwait(false);
                if (state is null) return;
                goto case "contentpersisted";

            case "contentpersisted":
                await DispatchWorkflowAsync(job, state, ctx, ct).ConfigureAwait(false);
                goto case "workflowrunning";

            case "workflowrunning":
                await PollExecutionAsync(job, ctx, ct).ConfigureAwait(false);
                return;

            default:
                await ctx.FailAsync($"Step desconhecido '{step}'.", null, permanent: true, ct).ConfigureAwait(false);
                return;
        }
    }

    // ── Etapa 1: download + detecta tipo ────────────────────────────────────
    private async Task<IngestionState?> DownloadAndDetectAsync(
        BackgroundResponseJob job, IngestionState state, IStandaloneJobContext ctx, CancellationToken ct)
    {
        await ctx.UpdateStepAsync(StepDownloading, ct).ConfigureAwait(false);

        DownloadedFile download;
        try
        {
            download = await _downloader.DownloadAsync(new Uri(state.Url!), state.Headers, ct).ConfigureAwait(false);
        }
        catch (IngestionRejectedException ex)
        {
            _logger.LogWarning("[Ingestion] download rejeitado job={JobId}: {Reason}", job.JobId, ex.Message);
            await ctx.FailAsync(ex.Message, null, permanent: true, ct).ConfigureAwait(false);
            return null;
        }

        await ctx.UpdateStepAsync(StepValidating, ct).ConfigureAwait(false);

        var urlPath = TryGetUrlPath(state.Url!);
        var type = FileTypeDetector.Detect(download.Bytes, download.ContentType, urlPath);
        if (type == DetectedFileType.Unsupported)
        {
            await ctx.FailAsync(
                "Tipo de arquivo não suportado. Apenas PDF, TXT e MD são aceitos.",
                null, permanent: true, ct).ConfigureAwait(false);
            return null;
        }

        var updated = state with
        {
            DetectedType = type.ToString(),
            ContentLength = download.ContentLength,
            ResponseContentType = download.ContentType,
            FinalUrl = download.FinalUrl.ToString(),
            DownloadedAt = DateTime.UtcNow,
        };

        if (type == DetectedFileType.Pdf)
        {
            // PDF bytes ficam em Redis com TTL longo o bastante pra cobrir todo
            // o lifetime do job. Cleanup explícito acontece após DI extrair com
            // sucesso (linha do ResolveContentAsync) ou via TTL natural.
            await CachePdfBytesAsync(job.JobId, download.Bytes, ct).ConfigureAwait(false);
            updated = updated with { HasCachedPdf = true };
        }
        else
        {
            // TXT/MD: decodifica UTF-8 já aqui e grava no estado — sem 2º
            // download na fase Extracting.
            string text;
            try { text = Encoding.UTF8.GetString(download.Bytes); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Ingestion] Decodificação UTF-8 falhou job={JobId}.", job.JobId);
                await ctx.FailAsync("Falha ao decodificar conteúdo como UTF-8.", null, permanent: true, ct)
                    .ConfigureAwait(false);
                return null;
            }
            updated = updated with { ExtractedContent = text, PageCount = 1 };
        }

        await ctx.UpdateIngestionContextAsync(SerializeContext(updated), ct).ConfigureAwait(false);
        return updated;
    }

    // ── Etapa 2: extrai (PDF via DI) ou aceita o que já existe (TXT/MD) ────
    private async Task<IngestionState?> ResolveContentAsync(
        BackgroundResponseJob job, IngestionState state, IStandaloneJobContext ctx, CancellationToken ct)
    {
        if (!Enum.TryParse<DetectedFileType>(state.DetectedType, ignoreCase: true, out var type)
            || type == DetectedFileType.Unsupported)
        {
            await ctx.FailAsync("DetectedType ausente/inválido no IngestionContext.", null, permanent: true, ct)
                .ConfigureAwait(false);
            return null;
        }

        // TXT/MD: ExtractedContent já foi populado em DownloadAndDetectAsync.
        // Em caso de retomada com ExtractedContent vazio (estado inconsistente),
        // re-baixa e decodifica.
        if (type is DetectedFileType.Text or DetectedFileType.Markdown)
        {
            if (string.IsNullOrEmpty(state.ExtractedContent))
            {
                try
                {
                    var download = await _downloader.DownloadAsync(new Uri(state.Url!), state.Headers, ct)
                        .ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(download.Bytes);
                    state = state with { ExtractedContent = text, PageCount = 1 };
                    await ctx.UpdateIngestionContextAsync(SerializeContext(state), ct).ConfigureAwait(false);
                }
                catch (IngestionRejectedException ex)
                {
                    var permanent = job.Attempt >= _poolOptions.MaxAttempts;
                    DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                    await ctx.FailAsync($"Re-download falhou: {ex.Message}", next, permanent, ct).ConfigureAwait(false);
                    return null;
                }
            }
            await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
            return state;
        }

        // PDF — já extraído em ciclo anterior?
        if (!string.IsNullOrEmpty(state.ExtractedContent))
        {
            await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
            return state;
        }

        await ctx.UpdateStepAsync(StepExtracting, ct).ConfigureAwait(false);

        byte[]? pdfBytes = null;

        // 1ª opção: Redis cache (caminho normal vindo do step Validating).
        if (state.HasCachedPdf)
        {
            pdfBytes = await TryFetchPdfFromCacheAsync(job.JobId).ConfigureAwait(false);
        }

        // 2ª opção: re-download (retomada após TTL expirar ou cache miss).
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            try
            {
                var download = await _downloader.DownloadAsync(new Uri(state.Url!), state.Headers, ct)
                    .ConfigureAwait(false);
                pdfBytes = download.Bytes;
            }
            catch (IngestionRejectedException ex)
            {
                var permanent = job.Attempt >= _poolOptions.MaxAttempts;
                DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                await ctx.FailAsync($"Re-download do PDF falhou: {ex.Message}", next, permanent, ct).ConfigureAwait(false);
                return null;
            }
        }

        DiAnalyzeResult result;
        try
        {
            result = await _diService.AnalyzeBytesAsync(
                pdfBytes,
                model: _diOptions.DefaultModel,
                features: null,
                outputFormat: "markdown",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Ingestion] Document Intelligence falhou job={JobId}.", job.JobId);
            var permanentDi = job.Attempt >= _poolOptions.MaxAttempts;
            DateTime? nextDi = permanentDi
                ? null
                : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
            await ctx.FailAsync($"Document Intelligence falhou: {ex.Message}", nextDi, permanentDi, ct)
                .ConfigureAwait(false);
            return null;
        }

        // PDF bytes não são mais necessários — libera Redis.
        await TryDeletePdfCacheAsync(job.JobId).ConfigureAwait(false);

        var updated = state with
        {
            ExtractedContent = result.Content,
            ExtractionId = result.OperationId,
            PageCount = result.PageCount,
            HasCachedPdf = false,
        };

        await ctx.UpdateIngestionContextAsync(SerializeContext(updated), ct).ConfigureAwait(false);
        await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
        return updated;
    }

    // ── Etapa 3: dispatch workflow com envelope contendo o conteúdo ─────────
    private async Task DispatchWorkflowAsync(
        BackgroundResponseJob job, IngestionState state, IStandaloneJobContext ctx, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(job.ExecutionId))
        {
            // Retomada pós-dispatch — workflow já foi disparado, só falta pollar.
            await ctx.UpdateStepAsync(StepWorkflowRunning, ct).ConfigureAwait(false);
            return;
        }

        var envelope = new IngestionWorkflowEnvelope
        {
            Content = state.ExtractedContent ?? string.Empty,
            ContentType = state.DetectedType ?? "Unknown",
            SourceUrl = state.FinalUrl ?? state.Url,
            ContentLength = state.ContentLength,
            PageCount = state.PageCount,
            Metadata = state.Metadata,
            ExtractionId = state.ExtractionId,
            OriginalInput = job.Input,
        };

        var envelopeJson = JsonSerializer.Serialize(envelope, JsonOpts);

        var metadata = new Dictionary<string, string>
        {
            ["standaloneJobId"] = job.JobId,
            ["ingestionContentType"] = state.DetectedType ?? "Unknown",
        };
        if (!string.IsNullOrEmpty(state.ExtractionId))
            metadata["ingestionExtractionId"] = state.ExtractionId;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        var executionId = await dispatcher.TriggerAsync(
            job.WorkflowId!,
            envelopeJson,
            metadata,
            source: ExecutionSource.Api,
            mode: ExecutionMode.Production,
            workflowVersionId: job.AgentVersionId,
            ct: ct).ConfigureAwait(false);

        await ctx.SetExecutionIdAsync(executionId, ct).ConfigureAwait(false);
        // Propaga pro objeto em memória pra que PollExecutionAsync (chamado no
        // mesmo tick via goto) enxergue o executionId recém-gravado.
        job.ExecutionId = executionId;
        await ctx.UpdateStepAsync(StepWorkflowRunning, ct).ConfigureAwait(false);
    }

    // ── Etapa 4: polla execução até terminal ────────────────────────────────
    private async Task PollExecutionAsync(BackgroundResponseJob job, IStandaloneJobContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ExecutionId))
        {
            await ctx.FailAsync("WorkflowRunning sem ExecutionId — estado inconsistente.", null, permanent: true, ct)
                .ConfigureAwait(false);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var executionRepo = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();

        var deadline = DateTime.UtcNow.AddMinutes(_poolOptions.JobMaxLifetimeMinutes);
        var pollDelay = TimeSpan.FromSeconds(2);
        var executionId = job.ExecutionId;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(pollDelay, ct).ConfigureAwait(false);
            var execution = await executionRepo.GetByIdAsync(executionId, ct).ConfigureAwait(false);
            if (execution is null) continue;

            switch (execution.Status)
            {
                case WorkflowStatus.Completed:
                    await ctx.CompleteAsync(execution.Output, ct).ConfigureAwait(false);
                    return;

                case WorkflowStatus.Failed:
                case WorkflowStatus.Cancelled:
                    var cancelled = execution.Status == WorkflowStatus.Cancelled;
                    var permanent = cancelled || job.Attempt >= _poolOptions.MaxAttempts;
                    DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                    var errMsg = execution.ErrorMessage ?? (cancelled ? "Execution cancelled" : "Execution failed");
                    await ctx.FailAsync(errMsg, next, permanent, ct).ConfigureAwait(false);
                    return;
            }
        }

        if (!ct.IsCancellationRequested)
        {
            await ctx.FailAsync(
                $"Job excedeu JobMaxLifetimeMinutes={_poolOptions.JobMaxLifetimeMinutes}m",
                null, permanent: true, ct).ConfigureAwait(false);
        }
    }

    // ── Helpers de Redis pros bytes do PDF ──────────────────────────────────
    private async Task CachePdfBytesAsync(string jobId, byte[] bytes, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(_poolOptions.JobMaxLifetimeMinutes + 5);
        try
        {
            await _cache.SetStringAsync(PdfRedisKeyPrefix + jobId, Convert.ToBase64String(bytes), ttl)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Falha de cache não é fatal — ResolveContentAsync re-baixa.
            _logger.LogWarning(ex, "[Ingestion] Falha ao cachear PDF bytes no Redis job={JobId}.", jobId);
        }
    }

    private async Task<byte[]?> TryFetchPdfFromCacheAsync(string jobId)
    {
        try
        {
            var cached = await _cache.GetStringAsync(PdfRedisKeyPrefix + jobId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(cached)) return null;
            return Convert.FromBase64String(cached);
        }
        catch (FormatException)
        {
            // IngestionContext corrompido ou operador editou à mão. Retorna null
            // pra que o caller force re-download.
            _logger.LogWarning("[Ingestion] PDF cache base64 corrompido job={JobId} — forçando re-download.", jobId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Ingestion] Falha ao ler PDF cache job={JobId}.", jobId);
            return null;
        }
    }

    private async Task TryDeletePdfCacheAsync(string jobId)
    {
        try { await _cache.RemoveAsync(PdfRedisKeyPrefix + jobId).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Ingestion] Cleanup do PDF cache falhou job={JobId} (TTL ainda cobre).", jobId);
        }
    }

    private static IngestionState? TryParseContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<IngestionState>(json, JsonOpts); }
        catch { return null; }
    }

    private static string SerializeContext(IngestionState state) =>
        JsonSerializer.Serialize(state, JsonOpts);

    private static string? TryGetUrlPath(string url)
    {
        try { return new Uri(url).AbsolutePath; }
        catch { return null; }
    }

    private int CalcBackoffSeconds(int attempt)
    {
        var baseSecs = Math.Max(1, _poolOptions.RetryBackoffBaseSeconds);
        var multiplier = Math.Pow(2, Math.Min(attempt, 10));
        return (int)(baseSecs * multiplier);
    }

    /// <summary>
    /// Persistido em <c>background_response_jobs.IngestionContext</c> entre
    /// steps. PDFs não vão aqui (vão pro Redis); só o flag <c>HasCachedPdf</c>
    /// indica se há bytes cacheados sob a chave derivada do JobId. Campos null
    /// são omitidos na serialização — JSONB fica enxuto.
    /// </summary>
    internal sealed record IngestionState(
        [property: JsonPropertyName("url")] string? Url = null,
        [property: JsonPropertyName("headers")] Dictionary<string, string>? Headers = null,
        [property: JsonPropertyName("metadata")] Dictionary<string, string>? Metadata = null,
        [property: JsonPropertyName("detectedType")] string? DetectedType = null,
        [property: JsonPropertyName("responseContentType")] string? ResponseContentType = null,
        [property: JsonPropertyName("finalUrl")] string? FinalUrl = null,
        [property: JsonPropertyName("contentLength")] long ContentLength = 0,
        [property: JsonPropertyName("downloadedAt")] DateTime? DownloadedAt = null,
        [property: JsonPropertyName("hasCachedPdf")] bool HasCachedPdf = false,
        [property: JsonPropertyName("extractedContent")] string? ExtractedContent = null,
        [property: JsonPropertyName("extractionId")] string? ExtractionId = null,
        [property: JsonPropertyName("pageCount")] int PageCount = 0);

    /// <summary>Shape do input passado ao workflow após a extração.</summary>
    private sealed class IngestionWorkflowEnvelope
    {
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
        [JsonPropertyName("contentType")] public string ContentType { get; init; } = string.Empty;
        [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; init; }
        [JsonPropertyName("contentLength")] public long ContentLength { get; init; }
        [JsonPropertyName("pageCount")] public int PageCount { get; init; }
        [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
        [JsonPropertyName("extractionId")] public string? ExtractionId { get; init; }
        [JsonPropertyName("originalInput")] public string? OriginalInput { get; init; }
    }
}

