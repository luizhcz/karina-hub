using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Wrapper do Azure Document Intelligence SDK.
/// Singleton — DocumentIntelligenceClient é thread-safe.
/// Segue pattern de AzureOpenAiClientProvider: credential + endpoint no constructor.
/// </summary>
public sealed class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly DocumentIntelligenceClient _client;
    private readonly ILogger<DocumentIntelligenceService> _logger;

    public DocumentIntelligenceService(
        IOptions<DocumentIntelligenceOptions> options,
        Azure.Core.TokenCredential credential,
        ILogger<DocumentIntelligenceService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var endpoint = new Uri(opts.Endpoint);

        _client = string.IsNullOrWhiteSpace(opts.ApiKey)
            ? new DocumentIntelligenceClient(endpoint, credential)
            : new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(opts.ApiKey));

        _logger.LogInformation("[DocIntel] Client criado para endpoint '{Endpoint}' (ManagedIdentity={UseMI}).",
            opts.Endpoint, string.IsNullOrWhiteSpace(opts.ApiKey));
    }

    public async Task<DiAnalyzeResult> AnalyzeAsync(
        Uri sourceUri,
        string model,
        string[]? features,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            model,
            sourceUri,
            ct);

        var result = operation.Value;
        var rawJson = JsonSerializer.Serialize(result);

        var pageCount = result.Pages?.Count ?? 0;
        var hasTables = result.Tables is { Count: > 0 };
        var hasHandwriting = result.Styles?.Any(s => s.IsHandwritten == true) ?? false;
        var primaryLanguage = result.Languages?.FirstOrDefault()?.Locale;

        sw.Stop();

        _logger.LogInformation("[DocIntel] Análise concluída: {Pages} páginas, {Tables} tabelas, {DurationMs}ms.",
            pageCount, result.Tables?.Count ?? 0, sw.ElapsedMilliseconds);

        return new DiAnalyzeResult(
            OperationId: operation.Id ?? Guid.NewGuid().ToString(),
            RawJson: rawJson,
            PageCount: pageCount,
            HasTables: hasTables,
            HasHandwriting: hasHandwriting,
            PrimaryLanguage: primaryLanguage,
            DurationMs: (int)sw.ElapsedMilliseconds);
    }
}
