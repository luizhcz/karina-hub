using System.Text.Json;
using Azure;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Functions;
using EfsAiHub.Platform.Runtime.Options;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
namespace EfsAiHub.Tests.Unit.Platform;

[Trait("Category", "Unit")]
public class DocumentIntelligenceFunctionsTests
{
    private readonly IDocumentExtractionRepository _repo = Substitute.For<IDocumentExtractionRepository>();
    private readonly IDocumentIntelligenceService _diService = Substitute.For<IDocumentIntelligenceService>();
    private readonly IEfsRedisCache _redis = Substitute.For<IEfsRedisCache>();
    private readonly IDatabase _redisDb = Substitute.For<IDatabase>();
    private readonly IDocumentIntelligencePricingCache _pricingCache = Substitute.For<IDocumentIntelligencePricingCache>();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();

    private readonly DocumentIntelligenceOptions _options = new()
    {
        Endpoint = "https://test.cognitiveservices.azure.com",
        DefaultModel = "prebuilt-layout",
        PollingTimeoutSeconds = 30,
        GateWaitTimeoutSeconds = 10,
        CacheTtlDays = 7,
    };

    private DocumentIntelligenceFunctions Build()
    {
        _redis.Database.Returns(_redisDb);
        _redis.BuildKey(Arg.Any<string>()).Returns(ci => "prefix:" + ci.Arg<string>());
        // Default pricing cache: retorna null → executor cai no fallback hardcoded.
        _pricingCache.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<DocumentIntelligencePricing?>(null));

        var opts = Substitute.For<IOptions<DocumentIntelligenceOptions>>();
        opts.Value.Returns(_options);

        return new DocumentIntelligenceFunctions(
            _repo,
            _diService,
            _redis,
            _pricingCache,
            opts,
            NullLogger<DocumentIntelligenceFunctions>.Instance,
            _httpClientFactory);
    }

    /// <summary>
    /// Minimal valid 1-page PDF.
    /// Generated once and captured as base64 to avoid runtime dependencies on PDF writers.
    /// </summary>
    private static byte[] MinimalPdf()
    {
        // Construct a minimal valid PDF-1.4 with precise xref offsets
        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.ASCII) { AutoFlush = true };

        w.Write("%PDF-1.4\n");
        var o1 = (int)ms.Position;
        w.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var o2 = (int)ms.Position;
        w.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        var o3 = (int)ms.Position;
        w.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");
        var xrefOffset = (int)ms.Position;
        w.Write("xref\n");
        w.Write("0 4\n");
        w.Write("0000000000 65535 f \r\n");
        w.Write($"{o1:D10} 00000 n \r\n");
        w.Write($"{o2:D10} 00000 n \r\n");
        w.Write($"{o3:D10} 00000 n \r\n");
        w.Write("trailer\n<< /Size 4 /Root 1 0 R >>\n");
        w.Write($"startxref\n{xrefOffset}\n");
        w.Write("%%EOF\n");

        return ms.ToArray();
    }

    private static string BuildInput(byte[]? pdfBytes = null, string model = "prebuilt-layout",
        bool cacheEnabled = true, string sourceType = "bytes")
    {
        pdfBytes ??= MinimalPdf();
        var source = sourceType == "bytes"
            ? new { type = "bytes", bytes = Convert.ToBase64String(pdfBytes), url = (string?)null }
            : new { type = sourceType, bytes = (string?)null, url = "https://blob.test/doc.pdf" };

        return JsonSerializer.Serialize(new
        {
            source,
            model,
            cacheEnabled,
        });
    }

    private void SetupExecutionContext()
    {
        var budget = new ExecutionBudget(100000, 10m);
        var ctx = new EfsAiHub.Core.Agents.Execution.ExecutionContext(
            ExecutionId: "exec-1",
            WorkflowId: "wf-1",
            Input: null,
            PromptVersions: new(),
            NodeCallback: null,
            Budget: budget,
            UserId: "user-1",
            ConversationId: "conv-1");
        DelegateExecutor.Current.Value = ctx;
    }

    private void SetupDiServiceSuccess(int pages = 1)
    {
        var diResult = new DiAnalyzeResult(
            OperationId: "op-123",
            RawJson: "{\"pages\":[]}",
            Content: "Texto extraído do PDF de teste.",
            PageCount: pages,
            HasTables: false,
            HasHandwriting: false,
            PrimaryLanguage: "pt",
            DurationMs: 500);

        _diService.AnalyzeAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<string[]?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(diResult);
        _diService.AnalyzeBytesAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string[]?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(diResult);
    }

    [Fact]
    public async Task Execute_ValidPdf_ReturnsSuccess()
    {
        SetupExecutionContext();
        SetupDiServiceSuccess();
        _redis.ExistsAsync(Arg.Any<string>()).Returns(false);
        _redisDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = Build();
        var result = await sut.ExecuteAsync(BuildInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("succeeded");
        doc.RootElement.GetProperty("cached").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("pageCount").GetInt32().Should().Be(1);

        await _diService.Received(1).AnalyzeBytesAsync(Arg.Any<byte[]>(), "prebuilt-layout", null, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).InsertJobAsync(Arg.Any<ExtractionJob>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).UpdateJobAsync(Arg.Any<ExtractionJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CacheHit_SkipsAzure()
    {
        SetupExecutionContext();
        _repo.LookupCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractionCacheEntry("sha", "prebuilt-layout", "none", "di:v1:sha:prebuilt-layout", 1, DateTime.UtcNow.AddDays(7)));
        _redis.ExistsAsync(Arg.Any<string>()).Returns(true);

        var sut = Build();
        var result = await sut.ExecuteAsync(BuildInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("cached");
        doc.RootElement.GetProperty("cached").GetBoolean().Should().BeTrue();

        await _diService.DidNotReceive().AnalyzeAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<string[]?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _diService.DidNotReceive().AnalyzeBytesAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string[]?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ExceedsFileSize_ReturnsError()
    {
        SetupExecutionContext();
        var tinyLimitOptions = new DocumentIntelligenceOptions
        {
            Endpoint = "https://test.cognitiveservices.azure.com",
            MaxFileSizeBytes = 10, // any valid PDF will exceed
        };
        _redis.Database.Returns(_redisDb);
        _redis.BuildKey(Arg.Any<string>()).Returns(ci => "prefix:" + ci.Arg<string>());
        var opts = Substitute.For<IOptions<DocumentIntelligenceOptions>>();
        opts.Value.Returns(tinyLimitOptions);

        var sut = new DocumentIntelligenceFunctions(
            _repo, _diService, _redis, _pricingCache, opts,
            NullLogger<DocumentIntelligenceFunctions>.Instance, _httpClientFactory);

        var result = await sut.ExecuteAsync(BuildInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("FILE_SIZE_EXCEEDED");
    }

    [Fact]
    public async Task Execute_CorruptPdf_ReturnsUnreadable()
    {
        SetupExecutionContext();

        var sut = Build();
        var corruptBytes = "not a pdf"u8.ToArray();
        var result = await sut.ExecuteAsync(BuildInput(pdfBytes: corruptBytes), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("UNREADABLE_PDF");
    }

    [Fact]
    public async Task Execute_AzureFailure_ReturnsError()
    {
        SetupExecutionContext();
        _redis.ExistsAsync(Arg.Any<string>()).Returns(false);
        _diService.AnalyzeBytesAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string[]?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<DiAnalyzeResult>(_ => throw new RequestFailedException(500, "Internal Server Error"));

        var sut = Build();
        var result = await sut.ExecuteAsync(BuildInput(), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("AZURE_DI_FAILURE");
    }

    [Fact]
    public async Task Execute_CacheDisabled_SkipsLookup()
    {
        SetupExecutionContext();
        SetupDiServiceSuccess();
        _redisDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = Build();
        var result = await sut.ExecuteAsync(BuildInput(cacheEnabled: false), CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("succeeded");

        await _repo.DidNotReceive().LookupCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CostIntegratesBudget()
    {
        SetupExecutionContext();
        SetupDiServiceSuccess(pages: 1);
        _redis.ExistsAsync(Arg.Any<string>()).Returns(false);
        _redisDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = Build();
        await sut.ExecuteAsync(BuildInput(), CancellationToken.None);

        var ctx = DelegateExecutor.Current.Value!;
        // prebuilt-layout = 0.01 per page, 1 page (from local PDF) = 0.01
        ctx.Budget.TotalCostUsd.Should().Be(0.01m);
    }

    [Fact]
    public async Task Execute_InvalidBase64_ReturnsUnreadable()
    {
        SetupExecutionContext();

        var sut = Build();
        var input = JsonSerializer.Serialize(new
        {
            source = new { type = "bytes", bytes = "!!!not-base64!!!", url = (string?)null },
            model = "prebuilt-layout",
            cacheEnabled = true,
        });

        var result = await sut.ExecuteAsync(input, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("UNREADABLE_PDF");
    }

    [Fact]
    public async Task Execute_UnsupportedSourceType_ReturnsError()
    {
        SetupExecutionContext();

        var sut = Build();
        var input = JsonSerializer.Serialize(new
        {
            source = new { type = "ftp", bytes = (string?)null, url = "ftp://test/doc.pdf" },
            model = "prebuilt-layout",
            cacheEnabled = true,
        });

        var result = await sut.ExecuteAsync(input, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("failed");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("SOURCE_UNAVAILABLE");
    }
}
