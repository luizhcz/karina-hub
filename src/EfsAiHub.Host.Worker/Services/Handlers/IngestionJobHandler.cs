using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Hashing;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Storage;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Ingestion;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services.Handlers;

/// <summary>
/// Handler de jobs de ingestão URL→PDF/PNG/JPEG/TXT/MD→workflow. Máquina de estados
/// persistida via <c>Step</c> + <c>IngestionContext</c>, idempotente por step:
/// crash do pod em qualquer ponto retoma de onde parou na próxima vez que o
/// reaper devolve o job pra Queued.
///
/// Política de storage: o texto extraído NÃO é persistido no JSONB do job — vive no
/// S3 (fonte canônica) e, pra PDF/imagem, também no cache do Document Intelligence
/// (rede de segurança). O JSONB guarda só o PONTEIRO do objeto, na pasta do workflow
/// (<c>{workflowId}/{sha256}.{ext}</c>): <c>RawObjectKey</c> (cru de TXT/MD) e
/// <c>ExtractedObjectKey</c> (texto extraído de PDF/PNG/JPEG). O ponteiro só é gravado
/// quando o PUT confirma (best-effort + anti-órfão). O texto chega ao workflow
/// hidratado em memória: no caminho contíguo segue do download/extração; numa retomada
/// pós-extração é lido do S3 pelo ponteiro, com fallback de re-download/re-extração
/// (cache do DI = hit barato). O binário cru (PDF/PNG/JPEG) nunca é gravado.
///
/// Steps:
/// <list type="bullet">
///   <item><c>null</c>/<c>"Queued"</c> → baixa o arquivo via <see cref="IngestionDownloader"/>.</item>
///   <item><c>"Downloading"</c>/<c>"Validating"</c> → detecta tipo (PDF/PNG/JPEG/TXT/MD);
///         imagem valida dimensão (50×50–10.000×10.000 px); TXT/MD → grava o cru no S3
///         (ponteiro <c>RawObjectKey</c>); PDF/imagem → não grava o cru (bytes seguem em
///         memória pra extração).</item>
///   <item><c>"Extracting"</c> (PDF/PNG/JPEG) → chama Document Intelligence (OCR) e grava
///         SÓ a saída de texto no S3 (ponteiro <c>ExtractedObjectKey</c>).
///         TXT/MD pulam direto pra <c>"ContentPersisted"</c>.</item>
///   <item><c>"ContentPersisted"</c> → hidrata o texto (memória/S3/re-extração) e
///         dispara workflow via <see cref="IWorkflowDispatcher"/> com o conteúdo +
///         metadata do cliente.</item>
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

    private static readonly JsonSerializerOptions JsonOpts = IngestionJsonDefaults.Options;

    private readonly IIngestionDownloader _downloader;
    private readonly IObjectStore _objectStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDocumentIntelligenceExtractor _extractor;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly StandalonePoolsOptions _poolOptions;
    private readonly IngestionApiOptions _ingestionOptions;
    private readonly ILogger<IngestionJobHandler> _logger;

    public IngestionJobHandler(
        IIngestionDownloader downloader,
        IObjectStore objectStore,
        IServiceScopeFactory scopeFactory,
        IDocumentIntelligenceExtractor extractor,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        IOptions<StandalonePoolsOptions> poolOptions,
        IOptions<IngestionApiOptions> ingestionOptions,
        ILogger<IngestionJobHandler> logger)
    {
        _downloader = downloader;
        _objectStore = objectStore;
        _scopeFactory = scopeFactory;
        _extractor = extractor;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
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

        // O workflowId vira a PASTA do arquivo cru no S3. Validar contra o contrato
        // de identidade (workflows legítimos já são forçados a esse charset na
        // criação) e FALHAR — em vez de sanitizar silenciosamente, o que colapsaria
        // ids distintos na mesma pasta. id fora do charset = request inválido.
        if (!IsValidWorkflowId(job.WorkflowId))
        {
            await ctx.FailAsync(
                $"WorkflowId inválido '{job.WorkflowId}' — esperado apenas [A-Za-z0-9_-].",
                null, permanent: true, ct).ConfigureAwait(false);
            return;
        }

        // Worker roda fora de HTTP request — IProjectContextAccessor/ITenantContextAccessor
        // (AsyncLocal) caem em Default e WorkflowService.TriggerAsync filtra workflows
        // Visibility=project pelo HasQueryFilter, fazendo workflows de outros projetos
        // sumirem ("workflow não encontrado"). Hidratamos a partir da row do job pra cobrir
        // todos os steps (download, DI, dispatch, polling) com o contexto correto.
        _projectAccessor.Current = new ProjectContext(job.ProjectId);
        _tenantAccessor.Current = new TenantContext(job.TenantId);

        var step = job.Step ?? string.Empty;

        // Retomada baseada no Step. Em fluxo "feliz" (sem crash) cada caso cai
        // pro próximo via labels; em retomada, salta pro step persistido.
        //
        // O texto extraído NÃO vive no JSONB: flui em memória pelo caminho contíguo.
        //  - preDownloaded: bytes do PDF do download (detecção) até a extração — o PDF
        //    cru NÃO vai pro S3. Em retomada (entra em validating/extracting) é null e
        //    o ResolveContentAsync re-baixa da origem.
        //  - resolvedContent: o texto já resolvido neste tick (TXT/MD decodificado ou
        //    saída da extração) — entregue ao DispatchWorkflowAsync sem re-ler o S3. Em
        //    retomada que entra direto em contentpersisted é null e o dispatch hidrata
        //    do S3 (ponteiro) com fallback de re-download/re-extração.
        byte[]? preDownloaded = null;
        string? resolvedContent = null;
        switch (step.ToLowerInvariant())
        {
            case "":
            case "queued":
            case "downloading":
                var (detected, downloadedBytes, downloadedText) =
                    await DownloadAndDetectAsync(job, state, ctx, ct).ConfigureAwait(false);
                if (detected is null) return;
                state = detected;
                preDownloaded = downloadedBytes;
                resolvedContent = downloadedText;
                goto case "validating";

            case "validating":
            {
                var (resolved, content) =
                    await ResolveContentAsync(job, state, ctx, ct, preDownloaded, resolvedContent).ConfigureAwait(false);
                if (resolved is null) return;
                state = resolved;
                resolvedContent = content;
                goto case "contentpersisted";
            }

            case "extracting":
            {
                // Retomada durante extração — re-resolve o conteúdo: re-baixa o PDF da
                // origem (não há cópia do cru no S3) e extrai.
                var (resolved, content) =
                    await ResolveContentAsync(job, state, ctx, ct, preDownloaded, resolvedContent).ConfigureAwait(false);
                if (resolved is null) return;
                state = resolved;
                resolvedContent = content;
                goto case "contentpersisted";
            }

            case "contentpersisted":
                if (!await DispatchWorkflowAsync(job, state, ctx, ct, resolvedContent).ConfigureAwait(false)) return;
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
    // Retorna o estado atualizado + o conteúdo que segue em memória pelo caminho
    // contíguo (sem re-ler o S3): PDF → os bytes crus pra extração (o PDF cru não vai
    // pro S3); TXT/MD → o texto decodificado pra montar o input do workflow.
    private async Task<(IngestionState? State, byte[]? PdfBytes, string? TextContent)> DownloadAndDetectAsync(
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
            return (null, null, null);
        }

        await ctx.UpdateStepAsync(StepValidating, ct).ConfigureAwait(false);

        var urlPath = TryGetUrlPath(state.Url!);
        var type = FileTypeDetector.Detect(download.Bytes, download.ContentType, urlPath);
        if (type == DetectedFileType.Unsupported)
        {
            await ctx.FailAsync(
                "Tipo de arquivo não suportado. Apenas PDF, PNG, JPEG, TXT e MD são aceitos.",
                null, permanent: true, ct).ConfigureAwait(false);
            return (null, null, null);
        }

        var updated = state with
        {
            DetectedType = type.ToString(),
            ContentLength = download.ContentLength,
            ResponseContentType = download.ContentType,
            FinalUrl = download.FinalUrl.ToString(),
            DownloadedAt = DateTime.UtcNow,
        };

        // Política de storage: só a representação em TEXTO vai pro S3, e o JSONB guarda
        // só o PONTEIRO (nunca o texto).
        //  - PDF/PNG/JPEG: o binário cru NÃO é gravado; os bytes seguem em memória pra
        //    extração (OCR via DI) e só o texto extraído vira objeto no bucket.
        //  - TXT/MD: o cru JÁ É o texto → grava no S3 (best-effort) + valida UTF-8; o
        //    texto segue em memória pro dispatch contíguo.
        // Key lógica '{workflowId}/{sha256}.{ext}'; o prefixo do bucket é aplicado no
        // store. A "pasta" do workflow é criada implicitamente pelo PUT (S3 não tem mkdir).
        byte[]? pdfBytes = null;
        string? textContent = null;
        if (type is DetectedFileType.Pdf or DetectedFileType.Png or DetectedFileType.Jpeg)
        {
            // Imagem: valida a dimensão (50×50–10.000×10.000 px, limite do Azure DI)
            // ANTES de seguir — fora do range falha cedo, sem gastar uma chamada ao DI.
            if (type is DetectedFileType.Png or DetectedFileType.Jpeg)
            {
                var size = ImageDimensions.TryRead(type, download.Bytes);
                if (size is null)
                {
                    await ctx.FailAsync("Imagem inválida ou corrompida (não foi possível ler as dimensões).",
                        null, permanent: true, ct).ConfigureAwait(false);
                    return (null, null, null);
                }
                if (!ImageDimensions.IsWithinLimits(size.Value))
                {
                    await ctx.FailAsync(
                        $"Dimensões {size.Value.Width}x{size.Value.Height}px fora do permitido " +
                        $"({ImageDimensions.MinDimension}x{ImageDimensions.MinDimension} a {ImageDimensions.MaxDimension}x{ImageDimensions.MaxDimension}).",
                        null, permanent: true, ct).ConfigureAwait(false);
                    return (null, null, null);
                }
            }

            // PDF/imagem seguem o mesmo caminho: bytes em memória pra extração; o cru
            // não vai pro S3.
            pdfBytes = download.Bytes;
        }
        else
        {
            // Decodifica ANTES do PUT: conteúdo inválido falha cedo, sem objeto inútil.
            string text;
            try { text = Encoding.UTF8.GetString(download.Bytes); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Ingestion] Decodificação UTF-8 falhou job={JobId}.", job.JobId);
                await ctx.FailAsync("Falha ao decodificar conteúdo como UTF-8.", null, permanent: true, ct)
                    .ConfigureAwait(false);
                return (null, null, null);
            }

            var sha256 = ContentHashCalculator.ComputeFromBytes(download.Bytes);
            var rawKey = BuildObjectKey(job.WorkflowId!, sha256, ExtForType(type));
            // Só grava o ponteiro se o PUT confirmar (anti-órfão). Se falhar, o ponteiro
            // fica null e a retomada cai no re-download; o texto em memória ainda atende
            // o dispatch contíguo deste tick.
            var stored = await _objectStore.PutAsync(rawKey, download.Bytes, download.ContentType, ct)
                .ConfigureAwait(false);
            updated = updated with { RawObjectKey = stored ? rawKey : null, PageCount = 1 };
            textContent = text;
        }

        await ctx.UpdateIngestionContextAsync(SerializeContext(updated), ct).ConfigureAwait(false);
        return (updated, pdfBytes, textContent);
    }

    // ── Etapa 2: extrai (PDF via DI) ou aceita o que já existe (TXT/MD) ────
    // Retorna o estado atualizado + o texto resolvido NESTE tick (pra o dispatch
    // contíguo não re-ler o S3). Devolve Content=null quando o conteúdo já está
    // durável no S3 mas não foi materializado aqui (retomada) — o dispatch hidrata
    // pelo ponteiro. preText/preDownloaded carregam o conteúdo do tick anterior do
    // caminho contíguo (download → resolve).
    private async Task<(IngestionState? State, string? Content)> ResolveContentAsync(
        BackgroundResponseJob job, IngestionState state, IStandaloneJobContext ctx, CancellationToken ct,
        byte[]? preDownloaded, string? preText)
    {
        if (!Enum.TryParse<DetectedFileType>(state.DetectedType, ignoreCase: true, out var type)
            || type == DetectedFileType.Unsupported)
        {
            await ctx.FailAsync("DetectedType ausente/inválido no IngestionContext.", null, permanent: true, ct)
                .ConfigureAwait(false);
            return (null, null);
        }

        // TXT/MD: o cru JÁ é o texto. A durabilidade é o RawObjectKey no S3.
        if (type is DetectedFileType.Text or DetectedFileType.Markdown)
        {
            // Caminho contíguo: o texto veio em memória do DownloadAndDetectAsync e o
            // cru já foi gravado (RawObjectKey). Segue sem re-ler nada.
            if (!string.IsNullOrEmpty(preText))
            {
                await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
                return (state, preText);
            }

            // Retomada com o cru durável no S3 → o dispatch hidrata pelo ponteiro.
            if (!string.IsNullOrEmpty(state.RawObjectKey))
            {
                await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
                return (state, null);
            }

            // Retomada sem ponteiro (o PUT original falhou): re-baixa, re-grava e segue.
            try
            {
                var download = await _downloader.DownloadAsync(new Uri(state.Url!), state.Headers, ct)
                    .ConfigureAwait(false);
                var text = Encoding.UTF8.GetString(download.Bytes);
                var sha256 = ContentHashCalculator.ComputeFromBytes(download.Bytes);
                var rawKey = BuildObjectKey(job.WorkflowId!, sha256, ExtForType(type));
                var stored = await _objectStore.PutAsync(rawKey, download.Bytes, download.ContentType, ct)
                    .ConfigureAwait(false);
                var refreshed = state with { RawObjectKey = stored ? rawKey : null, PageCount = 1 };
                await ctx.UpdateIngestionContextAsync(SerializeContext(refreshed), ct).ConfigureAwait(false);
                await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
                return (refreshed, text);
            }
            catch (IngestionRejectedException ex)
            {
                var permanent = job.Attempt >= _poolOptions.MaxAttempts;
                DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
                await ctx.FailAsync($"Re-download falhou: {ex.Message}", next, permanent, ct).ConfigureAwait(false);
                return (null, null);
            }
        }

        // PDF — já extraído em ciclo anterior? O sinal de "já extraído" é o ponteiro
        // do .txt no S3 (ExtractedObjectKey), não o texto. O dispatch hidrata pelo
        // ponteiro; não re-chama o Document Intelligence.
        if (!string.IsNullOrEmpty(state.ExtractedObjectKey))
        {
            await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
            return (state, null);
        }

        // Peek de capacidade ANTES de buscar/extrair o PDF: se o gate do Document
        // Intelligence já está cheio, não adianta puxar dezenas de MB da origem só pra
        // bater no gate e voltar pra fila. Advisory: o teto real é atômico dentro do
        // ExtractAsync, que ainda devolve GATE_TIMEOUT (mesmo caminho de espera) se
        // a vaga sumir entre o peek e a aquisição.
        if (!await _extractor.HasCapacityAsync(ct).ConfigureAwait(false))
        {
            await DeferForCapacityAsync(state, ctx, ExtractionErrorCode.GateTimeout, ct).ConfigureAwait(false);
            return (null, null);
        }

        await ctx.UpdateStepAsync(StepExtracting, ct).ConfigureAwait(false);

        // 1ª opção: bytes em memória do download de detecção (caminho contíguo, sem
        // crash). O PDF cru NÃO é guardado no S3 — só o .txt extraído (abaixo).
        byte[]? pdfBytes = preDownloaded;

        // 2ª opção: re-download da origem (retomada — preDownloaded é null).
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
                return (null, null);
            }
        }

        // Chamada via Extractor (não _diService direto) — garante que
        // jobs/events/cache em aihub.document_extraction_* são populados E que
        // cache HIT cross-ingestion (mesmo PDF, mesmo model) economiza o Azure.
        // Bug histórico (pré-refactor 2026-06): chamava _diService.AnalyzeBytesAsync
        // direto e perdia toda a observabilidade financeira/auditoria.
        // Modelo do DI escolhido pelo cliente via metadata "model" (default prebuilt-read).
        // O modelo acopla o formato de saída e a extensão no S3: prebuilt-layout→markdown
        // (.md), prebuilt-read→texto (.txt).
        var model = IngestionExtractionModel.Resolve(state.Metadata);

        ExtractionResult result;
        try
        {
            result = await _extractor.ExtractAsync(new ExtractionInput(
                Source: new ExtractionSource.Bytes(pdfBytes),
                // Schema legacy é intocável: ConversationId/UserId vão direto pras
                // colunas existentes em document_extraction_jobs. Sintético "ingestion:"
                // distingue do tool de agente sem precisar de coluna nova.
                ConversationId: $"ingestion:{job.JobId}",
                UserId: string.IsNullOrEmpty(job.AgentId) ? "ingestion" : job.AgentId,
                Model: model,
                OutputFormat: IngestionExtractionModel.OutputFormat(model),
                Features: null,
                CacheEnabled: true), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown do worker / cancelamento do dispatcher — NÃO consome attempt.
            // Não chama FailAsync: o job fica em Running com lease, e o StuckLeaseReaper
            // (se reativado) ou o restart limpo retoma do step persistido.
            throw;
        }
        catch (Exception ex)
        {
            // Apenas exceções catastróficas (Azure 401/403, falha de auth) propagam até aqui.
            // Erros previsíveis (PDF inválido, gate timeout, 429) vêm como
            // Status="failed" sem throw — tratados no bloco abaixo.
            _logger.LogWarning(ex, "[Ingestion] Document Intelligence falhou job={JobId}.", job.JobId);
            var permanentDi = job.Attempt >= _poolOptions.MaxAttempts;
            DateTime? nextDi = permanentDi
                ? null
                : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
            await ctx.FailAsync($"Document Intelligence falhou: {ex.Message}", nextDi, permanentDi, ct)
                .ConfigureAwait(false);
            return (null, null);
        }

        if (result.Status == "failed")
        {
            // Backpressure de capacidade (gate Document Intelligence cheio): a
            // extração nem chegou a rodar — não é falha do job, é falta de vaga
            // AGORA. Falta de capacidade é ESPERA, nunca erro: re-enfileira
            // (ctx.DeferAsync) sem consumir Attempt e SEM TETO — o job aguarda na
            // fila o tempo que for (minutos, horas, dias) até abrir vaga. A vez dele
            // chega pela ordem FIFO do TryLeaseAsync. DeferCount é só telemetria de
            // há quanto tempo está esperando; não decide mais vida/morte.
            if (ExtractionErrorCode.IsCapacityBackpressure(result.ErrorCode))
            {
                await DeferForCapacityAsync(state, ctx, result.ErrorCode!, ct).ConfigureAwait(false);
                return (null, null);
            }

            // Falha REAL do processamento (PDF quebrado, source 404, Azure 4xx/5xx).
            // Heurística de retry centralizada no domínio: ExtractionErrorCode.IsPermanent
            // mapeia códigos que NÃO ganham nada retentando (PDF quebrado, file size,
            // config error, source unavailable, page limit). Demais (Timeout/429/5xx
            // do Azure) contam contra MaxAttempts.
            var permanent = ExtractionErrorCode.IsPermanent(result.ErrorCode)
                || job.Attempt >= _poolOptions.MaxAttempts;
            DateTime? nextRetry = permanent
                ? null
                : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
            await ctx.FailAsync(
                $"Document Intelligence falhou ({result.ErrorCode}): {result.ErrorMessage}",
                nextRetry, permanent, ct).ConfigureAwait(false);
            return (null, null);
        }

        // Saída do DI → S3 durável: pasta do workflow + sha256 do PDF + extensão do
        // formato (.md p/ layout, .txt p/ read). O PDF cru NÃO é gravado (só o texto).
        // Best-effort: só grava o ponteiro se o PUT confirmar (anti-órfão). Se falhar,
        // ExtractedObjectKey fica null e a retomada re-extrai (cache do DI = hit barato);
        // o texto em memória ainda atende o dispatch contíguo deste tick.
        string? extractedKey = null;
        if (!string.IsNullOrEmpty(result.Content))
        {
            var sha256 = ContentHashCalculator.ComputeFromBytes(pdfBytes);
            var key = BuildObjectKey(job.WorkflowId!, sha256, IngestionExtractionModel.FileExtension(model));
            var stored = await _objectStore.PutAsync(key, Encoding.UTF8.GetBytes(result.Content),
                IngestionExtractionModel.ContentType(model), ct).ConfigureAwait(false);
            extractedKey = stored ? key : null;
        }

        var updated = state with
        {
            ExtractionId = result.OperationId,
            PageCount = result.PageCount,
            ExtractedObjectKey = extractedKey,
        };

        await ctx.UpdateIngestionContextAsync(SerializeContext(updated), ct).ConfigureAwait(false);
        await ctx.UpdateStepAsync(StepContentPersisted, ct).ConfigureAwait(false);
        return (updated, result.Content);
    }

    // ── Etapa 3: dispatch workflow com envelope contendo o conteúdo ─────────
    // Retorna true se o workflow foi (ou já estava) disparado → segue pro polling;
    // false se o tick terminou em espera (defer de capacidade) ou falha → para aqui.
    private async Task<bool> DispatchWorkflowAsync(
        BackgroundResponseJob job, IngestionState state, IStandaloneJobContext ctx, CancellationToken ct,
        string? freshContent)
    {
        if (!string.IsNullOrEmpty(job.ExecutionId))
        {
            // Retomada pós-dispatch — workflow já foi disparado, só falta pollar. Não
            // hidrata conteúdo à toa.
            await ctx.UpdateStepAsync(StepWorkflowRunning, ct).ConfigureAwait(false);
            return true;
        }

        // O texto não vive no JSONB: no caminho contíguo chega em memória (freshContent);
        // numa retomada pós-extração é hidratado do S3 (ponteiro) com fallback de
        // re-download/re-extração (cache do DI = hit barato).
        HydrationResult hydration;
        if (freshContent is not null)
        {
            MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "memory"));
            hydration = new HydrationResult(freshContent, CapacityWait: false);
        }
        else
        {
            hydration = await HydrateContentAsync(job, state, ct).ConfigureAwait(false);
        }

        if (hydration.CapacityWait)
        {
            // Sem vaga no gate pra re-extrair AGORA → espera, não erro. Volta pra fila
            // no Step ContentPersisted; ao reentrar, tenta hidratar de novo.
            await DeferForCapacityAsync(state, ctx, ExtractionErrorCode.GateTimeout, ct).ConfigureAwait(false);
            return false;
        }

        var content = hydration.Content;
        if (string.IsNullOrEmpty(content))
        {
            // Hidratação degenerada (S3 indisponível + cache expirado + origem
            // inacessível). NUNCA dispara o workflow com conteúdo vazio: falha com
            // retry (backoff) até MaxAttempts, depois permanente.
            var permanent = job.Attempt >= _poolOptions.MaxAttempts;
            DateTime? next = permanent ? null : DateTime.UtcNow.AddSeconds(CalcBackoffSeconds(job.Attempt));
            await ctx.FailAsync(
                "Não foi possível recuperar o conteúdo extraído (S3, cache e origem indisponíveis).",
                next, permanent, ct).ConfigureAwait(false);
            return false;
        }

        var envelope = new IngestionWorkflowEnvelope
        {
            Content = content,
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
        return true;
    }

    // ── Hidratação do texto extraído pro input do workflow ──────────────────
    // Usada só quando o texto NÃO está em memória (retomada que entra direto no
    // dispatch). Cadeia: (1) S3 pelo ponteiro persistido; (2) miss → re-download da
    // origem e, pra PDF, re-extração via cache do DI (hit = custo 0); re-popula o S3
    // best-effort. CapacityWait=true quando o gate está cheio pra re-extrair agora
    // (espera, não erro). Content=null quando nada recuperou (S3 + cache + origem).
    private async Task<HydrationResult> HydrateContentAsync(
        BackgroundResponseJob job, IngestionState state, CancellationToken ct)
    {
        Enum.TryParse<DetectedFileType>(state.DetectedType, ignoreCase: true, out var type);
        // PDF/PNG/JPEG passam por OCR no DI → ponteiro do texto extraído (ExtractedObjectKey).
        // TXT/MD são texto cru → ponteiro do cru (RawObjectKey).
        var needsExtraction = type is DetectedFileType.Pdf or DetectedFileType.Png or DetectedFileType.Jpeg;
        var key = needsExtraction ? state.ExtractedObjectKey : state.RawObjectKey;

        // 1) S3 pelo ponteiro persistido — caminho normal de retomada.
        if (!string.IsNullOrEmpty(key))
        {
            var bytes = await _objectStore.GetAsync(key, ct).ConfigureAwait(false);
            if (bytes is not null)
            {
                MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "s3"));
                return new HydrationResult(Encoding.UTF8.GetString(bytes), CapacityWait: false);
            }
        }

        // 2) Fallback: objeto ausente no S3 (PUT falhou na época, ou sumiu). Re-baixa
        //    da origem (idempotente). TXT/MD já é texto; PDF/imagem precisam re-extrair.
        byte[] pdfBytes;
        try
        {
            var download = await _downloader.DownloadAsync(new Uri(state.Url!), state.Headers, ct)
                .ConfigureAwait(false);
            if (!needsExtraction)
            {
                var text = Encoding.UTF8.GetString(download.Bytes);
                var sha = ContentHashCalculator.ComputeFromBytes(download.Bytes);
                await _objectStore.PutAsync(BuildObjectKey(job.WorkflowId!, sha, ExtForType(type)),
                    download.Bytes, download.ContentType, ct).ConfigureAwait(false);
                MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "reextract"));
                return new HydrationResult(text, CapacityWait: false);
            }
            pdfBytes = download.Bytes;
        }
        catch (IngestionRejectedException ex)
        {
            _logger.LogWarning(ex, "[Ingestion] Hidratação: re-download falhou job={JobId}.", job.JobId);
            MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "failed"));
            return new HydrationResult(null, CapacityWait: false);
        }

        // PDF: re-extrai via cache do DI (hit por sha256 = custo 0, sem slot). Peek de
        // capacidade antes — gate cheio é espera, não erro.
        if (!await _extractor.HasCapacityAsync(ct).ConfigureAwait(false))
        {
            MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "capacity_wait"));
            return new HydrationResult(null, CapacityWait: true);
        }

        var model = IngestionExtractionModel.Resolve(state.Metadata);

        ExtractionResult result;
        try
        {
            result = await _extractor.ExtractAsync(new ExtractionInput(
                Source: new ExtractionSource.Bytes(pdfBytes),
                ConversationId: $"ingestion:{job.JobId}",
                UserId: string.IsNullOrEmpty(job.AgentId) ? "ingestion" : job.AgentId,
                Model: model,
                OutputFormat: IngestionExtractionModel.OutputFormat(model),
                Features: null,
                CacheEnabled: true), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Ingestion] Hidratação: re-extração falhou job={JobId}.", job.JobId);
            MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "failed"));
            return new HydrationResult(null, CapacityWait: false);
        }

        if (result.Status == "failed")
        {
            var capacityWait = ExtractionErrorCode.IsCapacityBackpressure(result.ErrorCode);
            MetricsRegistry.IngestionContentHydrations.Add(1,
                new KeyValuePair<string, object?>("source", capacityWait ? "capacity_wait" : "failed"));
            return new HydrationResult(null, CapacityWait: capacityWait);
        }

        // Re-popula o S3 best-effort pra próximas retomadas não re-extraírem.
        if (!string.IsNullOrEmpty(result.Content))
        {
            var sha = ContentHashCalculator.ComputeFromBytes(pdfBytes);
            await _objectStore.PutAsync(BuildObjectKey(job.WorkflowId!, sha, IngestionExtractionModel.FileExtension(model)),
                Encoding.UTF8.GetBytes(result.Content), IngestionExtractionModel.ContentType(model), ct).ConfigureAwait(false);
        }
        MetricsRegistry.IngestionContentHydrations.Add(1, new KeyValuePair<string, object?>("source", "reextract"));
        return new HydrationResult(result.Content, CapacityWait: false);
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

    // ── Key de objeto no S3: pasta = workflowId, nome = sha256 do conteúdo ──
    private static string BuildObjectKey(string workflowId, string sha256, string ext) =>
        $"{SanitizeKeySegment(workflowId)}/{sha256}.{ext}";

    // Extensão do CRU recebido (só TXT/MD são gravados crus; PDF vira .txt extraído).
    private static string ExtForType(DetectedFileType type) => type switch
    {
        DetectedFileType.Markdown => "md",
        DetectedFileType.Text => "txt",
        _ => "bin",
    };

    // Charset do contrato de identidade do projeto (^[A-Za-z0-9_-]+$). Validado em
    // ProcessAsync ANTES de qualquer trabalho — id fora disso falha o job.
    private static bool IsValidWorkflowId(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Defesa-em-profundidade: o workflowId já foi validado em ProcessAsync, mas
    // sanitizar aqui garante que a key do S3 nunca contenha path injection ('/',
    // '..') mesmo se este método for chamado por um caminho não validado.
    private static string SanitizeKeySegment(string raw)
    {
        var cleaned = new string(raw.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return cleaned.Length == 0 ? "unknown" : cleaned;
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

    // Backoff de espera por capacidade: plano (não exponencial) porque não
    // representa "falhas acumuladas" e sim "aguardando vaga" — não faz sentido
    // recuar cada vez mais numa espera legítima. Jitter espalha o thundering herd
    // quando vários jobs caem no mesmo gate cheio e voltam pra fila no mesmo
    // instante (senão re-disputam a vaga todos juntos no mesmo tick).
    private int CalcCapacityWaitBackoffSeconds()
    {
        var baseSecs = Math.Max(1, _poolOptions.CapacityWaitBackoffSeconds);
        return baseSecs + Random.Shared.Next(0, baseSecs + 1);
    }

    // Falta de capacidade do Document Intelligence é ESPERA, não erro: devolve o
    // job pra fila (DeferAsync — sem consumir Attempt, sem teto) até abrir vaga.
    // Compartilhado pelo peek pré-download e pelo retorno GATE_TIMEOUT do
    // ExtractAsync. DeferCount é só telemetria de "há quanto tempo espera".
    private async Task<IngestionState?> DeferForCapacityAsync(
        IngestionState state, IStandaloneJobContext ctx, string errorCode, CancellationToken ct)
    {
        MetricsRegistry.IngestionCapacityWaits.Add(1,
            new KeyValuePair<string, object?>("error_code", errorCode));

        var deferred = state with { DeferCount = state.DeferCount + 1 };
        // Persiste o contador ANTES do defer: UpdateIngestionContext exige
        // Status='Running' (ownership), e o defer logo abaixo vira Queued.
        await ctx.UpdateIngestionContextAsync(SerializeContext(deferred), ct).ConfigureAwait(false);
        var deferUntil = DateTime.UtcNow.AddSeconds(CalcCapacityWaitBackoffSeconds());
        await ctx.DeferAsync(
            $"Document Intelligence sem capacidade ({errorCode}) — aguardando vaga na fila (espera #{deferred.DeferCount}).",
            deferUntil, ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Persistido em <c>background_response_jobs.IngestionContext</c> entre steps.
    /// Nem bytes nem TEXTO vão aqui — o conteúdo extraído vive no S3 (e, pra PDF, no
    /// cache do Document Intelligence). O JSONB guarda só os PONTEIROS lógicos:
    /// <c>RawObjectKey</c> (cru de TXT/MD) e <c>ExtractedObjectKey</c> (.txt extraído
    /// de PDF), preenchidos só quando o PUT confirma. Campos null são omitidos na
    /// serialização — JSONB enxuto e de tamanho ~constante. Linhas legadas que ainda
    /// tenham um <c>extractedContent</c> inline são desserializadas sem erro (o membro
    /// desconhecido é ignorado) e o campo some na próxima reescrita.
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
        [property: JsonPropertyName("rawObjectKey")] string? RawObjectKey = null,
        [property: JsonPropertyName("extractedObjectKey")] string? ExtractedObjectKey = null,
        [property: JsonPropertyName("extractionId")] string? ExtractionId = null,
        [property: JsonPropertyName("pageCount")] int PageCount = 0,
        // Quantas vezes o job foi re-enfileirado esperando capacidade do Document
        // Intelligence (gate cheio). Pura telemetria de "há quanto tempo espera" —
        // backpressure não consome Attempt e não tem teto; o job aguarda até abrir
        // vaga. Cresce sem limite em saturação prolongada (apenas um int no JSONB).
        [property: JsonPropertyName("deferCount")] int DeferCount = 0);

    /// <summary>
    /// Resultado da hidratação do texto pro input do workflow. <c>Content</c> null +
    /// <c>CapacityWait</c> false = falha real (S3 + cache + origem indisponíveis);
    /// <c>CapacityWait</c> true = sem vaga no gate pra re-extrair agora (espera, não erro).
    /// </summary>
    private readonly record struct HydrationResult(string? Content, bool CapacityWait);

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

