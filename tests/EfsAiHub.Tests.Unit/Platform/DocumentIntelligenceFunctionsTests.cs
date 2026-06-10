using System.Text.Json;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Platform;

/// <summary>
/// Testa o adapter JSON-in/JSON-out <see cref="DocumentIntelligenceFunctions"/>.
/// Pós-refactor 2026-06 o adapter é fino: traduz o request do agente, delega ao
/// <see cref="IDocumentIntelligenceExtractor"/> (pipeline canônico) e serializa
/// o resultado. Logo o teste mocka o extractor e cobre só as responsabilidades
/// do adapter: parse, tradução de source, mapeamento de erro e shape da resposta.
///
/// A lógica interna do pipeline (cache, gate de concorrência, Azure DI, pricing)
/// vive em DocumentIntelligenceExtractor e não é exercida aqui — ela depende do
/// wrapper concreto DocumentIntelligenceService, que não tem seam de mock; sua
/// cobertura é alvo de teste de integração separado.
/// </summary>
[Trait("Category", "Unit")]
public class DocumentIntelligenceFunctionsTests
{
    private readonly IDocumentIntelligenceExtractor _extractor = Substitute.For<IDocumentIntelligenceExtractor>();

    private DocumentIntelligenceFunctions Build()
    {
        // Caminho não-chat: sem ExecutionContext, o adapter cai em "unknown" pros
        // campos de auditoria — suficiente pros testes do adapter.
        DelegateExecutor.Current.Value = null;
        return new DocumentIntelligenceFunctions(_extractor, NullLogger<DocumentIntelligenceFunctions>.Instance);
    }

    private static ExtractionResult Failed(string errorCode, string message) => new(
        JobId: Guid.NewGuid(), Status: "failed", Content: null, ResultRef: null,
        PageCount: 0, CostUsd: 0m, FromCache: false, OperationId: null, DurationMs: null,
        ErrorCode: errorCode, ErrorMessage: message, ErrorDetail: null);

    private static ExtractionResult Succeeded() => new(
        JobId: Guid.NewGuid(), Status: "succeeded", Content: "conteúdo extraído",
        ResultRef: "di:v2:abc:prebuilt-layout:markdown", PageCount: 3, CostUsd: 0.03m,
        FromCache: false, OperationId: "op-1", DurationMs: 500,
        ErrorCode: null, ErrorMessage: null, ErrorDetail: null);

    private static string BytesInput(byte[]? bytes = null, string model = "prebuilt-layout")
    {
        bytes ??= new byte[] { 1, 2, 3, 4 };
        return JsonSerializer.Serialize(new
        {
            source = new { type = "bytes", bytes = Convert.ToBase64String(bytes), url = "" },
            model,
            cacheEnabled = true,
        });
    }

    [Fact]
    public async Task Execute_ExtractorSucceeds_ReturnsSuccessShape()
    {
        _extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
            .Returns(Succeeded());

        var result = await Build().ExecuteAsync(BytesInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("succeeded");
        doc.RootElement.GetProperty("pageCount").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("content").GetString().Should().Be("conteúdo extraído");
        doc.RootElement.GetProperty("resultRef").GetString().Should().Be("di:v2:abc:prebuilt-layout:markdown");
    }

    [Fact]
    public async Task Execute_PassesRequestedModelToExtractor()
    {
        _extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
            .Returns(Succeeded());

        await Build().ExecuteAsync(BytesInput(model: "prebuilt-invoice"), CancellationToken.None);

        await _extractor.Received(1).ExtractAsync(
            Arg.Is<ExtractionInput>(i => i.Model == "prebuilt-invoice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_GateTimeout_PropagatesFailedShape()
    {
        // O gate de concorrência cheio chega ao adapter como Status="failed" +
        // ErrorCode=GATE_TIMEOUT (o pipeline nunca lança nesse caso). O adapter
        // repassa fielmente — quem decide re-enfileirar é o caller (ingestão).
        _extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
            .Returns(Failed(ExtractionErrorCode.GateTimeout, "Capacidade global esgotada."));

        var result = await Build().ExecuteAsync(BytesInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("GATE_TIMEOUT");
        doc.RootElement.GetProperty("errorMessage").GetString().Should().Contain("Capacidade");
    }

    [Fact]
    public async Task Execute_InvalidBase64_ReturnsUnreadable_WithoutCallingExtractor()
    {
        var input = JsonSerializer.Serialize(new
        {
            source = new { type = "bytes", bytes = "!!!not-base64!!!", url = (string?)null },
            model = "prebuilt-layout",
            cacheEnabled = true,
        });

        var result = await Build().ExecuteAsync(input, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("UNREADABLE_PDF");
        await _extractor.DidNotReceive().ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_MissingUrl_ReturnsSourceUnavailable()
    {
        var input = JsonSerializer.Serialize(new
        {
            source = new { type = "blobUrl", bytes = (string?)null, url = "" },
            model = "prebuilt-layout",
            cacheEnabled = true,
        });

        var result = await Build().ExecuteAsync(input, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("SOURCE_UNAVAILABLE");
    }

    [Fact]
    public async Task Execute_UnsupportedSourceType_ReturnsSourceUnavailable()
    {
        var input = JsonSerializer.Serialize(new
        {
            source = new { type = "ftp", bytes = (string?)null, url = "ftp://test/doc.pdf" },
            model = "prebuilt-layout",
            cacheEnabled = true,
        });

        var result = await Build().ExecuteAsync(input, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("SOURCE_UNAVAILABLE");
    }
}
