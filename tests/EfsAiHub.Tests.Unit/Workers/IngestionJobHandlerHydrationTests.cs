using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Storage;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Host.Worker.Services.Handlers;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Ingestion;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Workers;

/// <summary>
/// Cobre a externalização do texto extraído: o JSONB do job guarda só o PONTEIRO
/// (nunca o texto), e o conteúdo é hidratado em memória no dispatch — do caminho
/// contíguo, do S3 pelo ponteiro, ou re-extraído via cache do DI. Foca na matriz
/// retomada × fonte e nas invariantes "nunca dispara vazio", "falta de capacidade é
/// espera" e "ponteiro só quando o PUT confirma" (anti-órfão).
/// </summary>
public sealed class IngestionJobHandlerHydrationTests
{
    // ── Gate de migração: desserializar JSONB legado não pode quebrar ────────
    [Fact]
    public void JsonDefaults_nao_proibem_membros_desconhecidos_e_omitem_nulls()
    {
        var opts = IngestionJsonDefaults.Options;

        opts.UnmappedMemberHandling.Should().NotBe(JsonUnmappedMemberHandling.Disallow);
        opts.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
    }

    // ── Contrato de serialização: ponteiros sim, texto nunca ────────────────
    [Fact]
    public void Serializar_estado_emite_ponteiros_e_nunca_extractedContent()
    {
        var state = new IngestionJobHandler.IngestionState(
            Url: "https://x/y.pdf", DetectedType: "Pdf",
            RawObjectKey: "wf/raw.txt", ExtractedObjectKey: "wf/abc.txt");

        var json = JsonSerializer.Serialize(state, IngestionJsonDefaults.Options);

        json.Should().Contain("extractedObjectKey");
        json.Should().Contain("rawObjectKey");
        json.Should().NotContain("extractedContent");
    }

    [Fact]
    public void Desserializar_JSONB_legado_com_extractedContent_inline_nao_lanca_e_ignora_o_campo()
    {
        const string legacy =
            "{\"url\":\"https://x/y.pdf\",\"detectedType\":\"Pdf\"," +
            "\"extractedObjectKey\":\"wf/abc.txt\"," +
            "\"extractedContent\":\"TEXTO ANTIGO INLINE\",\"pageCount\":3}";

        var state = JsonSerializer.Deserialize<IngestionJobHandler.IngestionState>(legacy, IngestionJsonDefaults.Options);

        state.Should().NotBeNull();
        state!.ExtractedObjectKey.Should().Be("wf/abc.txt");
        state.PageCount.Should().Be(3);
    }

    // ── Retomada × fonte ─────────────────────────────────────────────────────
    [Fact]
    public async Task Resume_em_extracting_sem_capacidade_aguarda_sem_extrair_nem_falhar()
    {
        var h = new Harness();
        h.Extractor.HasCapacityAsync(Arg.Any<CancellationToken>()).Returns(false);
        var handler = h.Build();
        var job = Job("Extracting", new IngestionJobHandler.IngestionState(Url: "https://x/y.pdf", DetectedType: "Pdf"));

        await handler.ProcessAsync(job, h.Ctx, CancellationToken.None);

        await h.Ctx.Received(1).DeferAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await h.Ctx.DidNotReceive().FailAsync(Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await h.Extractor.DidNotReceive().ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resume_PDF_ja_extraido_hidrata_do_S3_sem_chamar_o_DI()
    {
        var h = new Harness();
        h.ObjectStore.GetAsync("wf/abc.txt", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("HELLO FROM S3"));
        h.ExecutionCompletes();
        var handler = h.Build();
        var job = Job("Extracting", new IngestionJobHandler.IngestionState(
            Url: "https://x/y.pdf", DetectedType: "Pdf", ExtractedObjectKey: "wf/abc.txt"));

        await handler.ProcessAsync(job, h.Ctx, CancellationToken.None);

        await h.Extractor.DidNotReceive().ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>());
        await h.ObjectStore.Received().GetAsync("wf/abc.txt", Arg.Any<CancellationToken>());
        h.DispatchedPayload.Should().Contain("HELLO FROM S3");
    }

    [Fact]
    public async Task Resume_PDF_com_S3_miss_reextrai_via_cache_e_repopula_o_S3()
    {
        var h = new Harness();
        h.ObjectStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        h.Downloader.DownloadAsync(Arg.Any<Uri>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadedFile(Encoding.ASCII.GetBytes("%PDF-1.4"), "application/pdf", new Uri("https://x/y.pdf"), 8));
        h.Extractor.HasCapacityAsync(Arg.Any<CancellationToken>()).Returns(true);
        h.Extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractionResult(Guid.NewGuid(), "succeeded", "REEXTRACTED", null, 1, 0m, true, "op", 10, null, null, null));
        h.ObjectStore.PutAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        h.ExecutionCompletes();
        var handler = h.Build();
        var job = Job("Extracting", new IngestionJobHandler.IngestionState(
            Url: "https://x/y.pdf", DetectedType: "Pdf", ExtractedObjectKey: "wf/abc.txt"));

        await handler.ProcessAsync(job, h.Ctx, CancellationToken.None);

        await h.Extractor.Received(1).ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>());
        h.DispatchedPayload.Should().Contain("REEXTRACTED");
        await h.ObjectStore.Received().PutAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hidratacao_degenerada_falha_com_retry_e_nunca_dispara_workflow_vazio()
    {
        var h = new Harness();
        h.ObjectStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        h.Downloader.DownloadAsync(Arg.Any<Uri>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns<DownloadedFile>(_ => throw new IngestionRejectedException("origem 404"));
        var handler = h.Build();
        var job = Job("ContentPersisted", new IngestionJobHandler.IngestionState(Url: "https://x/y.pdf", DetectedType: "Pdf"));

        await handler.ProcessAsync(job, h.Ctx, CancellationToken.None);

        await h.Ctx.Received().FailAsync(Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await h.Dispatcher.DidNotReceive().TriggerAsync(Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<Dictionary<string, string>?>(), Arg.Any<ExecutionSource>(), Arg.Any<ExecutionMode>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PUT_que_falha_nao_grava_ponteiro_orfao_e_dispara_do_conteudo_em_memoria()
    {
        var h = new Harness();
        h.Downloader.DownloadAsync(Arg.Any<Uri>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadedFile(Encoding.ASCII.GetBytes("%PDF-1.4"), "application/pdf", new Uri("https://x/y.pdf"), 8));
        h.Extractor.HasCapacityAsync(Arg.Any<CancellationToken>()).Returns(true);
        h.Extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractionResult(Guid.NewGuid(), "succeeded", "FRESH TEXT", null, 1, 0m, false, "op", 10, null, null, null));
        h.ObjectStore.PutAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);
        h.ExecutionCompletes();
        var handler = h.Build();
        var job = Job("Extracting", new IngestionJobHandler.IngestionState(Url: "https://x/y.pdf", DetectedType: "Pdf"));

        await handler.ProcessAsync(job, h.Ctx, CancellationToken.None);

        h.DispatchedPayload.Should().Contain("FRESH TEXT");
        h.PersistedContexts.Should().NotBeEmpty();
        h.PersistedContexts[^1].Should().NotContain("extractedObjectKey");
    }

    // ── Harness ──────────────────────────────────────────────────────────────
    private sealed class Harness
    {
        public readonly IIngestionDownloader Downloader = Substitute.For<IIngestionDownloader>();
        public readonly IObjectStore ObjectStore = Substitute.For<IObjectStore>();
        public readonly IDocumentIntelligenceExtractor Extractor = Substitute.For<IDocumentIntelligenceExtractor>();
        public readonly IWorkflowDispatcher Dispatcher = Substitute.For<IWorkflowDispatcher>();
        public readonly IWorkflowExecutionRepository ExecutionRepo = Substitute.For<IWorkflowExecutionRepository>();
        public readonly IStandaloneJobContext Ctx = Substitute.For<IStandaloneJobContext>();
        public readonly List<string?> PersistedContexts = new();
        public string? DispatchedPayload;

        public IngestionJobHandler Build()
        {
            var sp = Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IWorkflowDispatcher)).Returns(Dispatcher);
            sp.GetService(typeof(IWorkflowExecutionRepository)).Returns(ExecutionRepo);
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);

            Ctx.CompleteAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
            Ctx.FailAsync(Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(true);
            Ctx.DeferAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
            Ctx.When(c => c.UpdateIngestionContextAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()))
               .Do(ci => PersistedContexts.Add((string?)ci[0]));

            Dispatcher.TriggerAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
                    Arg.Any<ExecutionSource>(), Arg.Any<ExecutionMode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(ci => { DispatchedPayload = (string?)ci[1]; return "exec-1"; });

            return new IngestionJobHandler(
                Downloader, ObjectStore, scopeFactory, Extractor,
                Substitute.For<IProjectContextAccessor>(), Substitute.For<ITenantContextAccessor>(),
                Options.Create(new DocumentIntelligenceOptions { DefaultModel = "prebuilt-layout" }),
                Options.Create(new StandalonePoolsOptions { Enabled = true, MaxAttempts = 3, JobMaxLifetimeMinutes = 1 }),
                Options.Create(new IngestionApiOptions { Enabled = true }),
                NullLogger<IngestionJobHandler>.Instance);
        }

        public void ExecutionCompletes() =>
            ExecutionRepo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowExecution
                {
                    ExecutionId = "exec-1",
                    WorkflowId = "wf",
                    Status = WorkflowStatus.Completed,
                    Output = "ok",
                });
    }

    private static BackgroundResponseJob Job(string step, IngestionJobHandler.IngestionState state) => new()
    {
        JobId = "job-1",
        WorkflowId = "wf",
        ProjectId = "p",
        TenantId = "t",
        Step = step,
        Attempt = 0,
        IngestionContext = JsonSerializer.Serialize(state, IngestionJsonDefaults.Options),
    };
}
