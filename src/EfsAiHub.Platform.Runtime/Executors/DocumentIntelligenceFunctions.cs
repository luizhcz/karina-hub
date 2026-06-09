using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Execution;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Executors;

/// <summary>
/// Adapter JSON-in/JSON-out que registra o tool <c>document_intelligence</c>
/// no <c>CodeExecutorRegistry</c>. Recebe um <see cref="ExtractionRequest"/>
/// serializado pelo agente, traduz pro shape do <see cref="IDocumentIntelligenceExtractor"/>,
/// e retorna o resultado serializado.
///
/// Responsabilidades exclusivas:
/// <list type="bullet">
///   <item>Deserializar JSON do agente.</item>
///   <item>Traduzir <c>DocumentSource</c> em <see cref="ExtractionSource"/>.</item>
///   <item>Resolver ConversationId/UserId via <see cref="DelegateExecutor.Current"/>.</item>
///   <item>Integrar <c>result.CostUsd</c> no <c>ExecutionBudget</c> do workflow.</item>
///   <item>Serializar resposta no formato JSON esperado pelo agente.</item>
/// </list>
/// </summary>
public class DocumentIntelligenceFunctions
{
    private readonly IDocumentIntelligenceExtractor _extractor;
    private readonly ILogger<DocumentIntelligenceFunctions> _logger;

    public DocumentIntelligenceFunctions(
        IDocumentIntelligenceExtractor extractor,
        ILogger<DocumentIntelligenceFunctions> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(string input, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<ExtractionRequest>(input, JsonDefaults.CaseInsensitive)
            ?? throw new InvalidOperationException("Input JSON inválido para ExtractionRequest.");

        var ctx = DelegateExecutor.Current.Value;
        var conversationId = ctx?.ConversationId;
        var userId = ctx?.UserId;

        // Guard: schema NOT NULL não aceita strings vazias. Workflows fora de
        // chat (eval/batch) podem cair aqui — usamos "unknown" pra evitar crash
        // do INSERT, mantendo o job rastreável via jobId.
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning(
                "[DocIntel] ExecutionContext sem ConversationId/UserId — usando 'unknown'. " +
                "Workflow rodando fora de chat (eval/batch)?");
            conversationId = string.IsNullOrWhiteSpace(conversationId) ? "unknown" : conversationId;
            userId = string.IsNullOrWhiteSpace(userId) ? "unknown" : userId;
        }

        ExtractionSource source;
        if (string.Equals(request.Source.Type, "blobUrl", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Source.Url))
                return BuildErrorJson(Guid.Empty, ExtractionErrorCode.SourceUnavailable, "URL não fornecida.");
            try
            {
                source = new ExtractionSource.Url(new Uri(request.Source.Url));
            }
            catch (UriFormatException ex)
            {
                return BuildErrorJson(Guid.Empty, ExtractionErrorCode.SourceUnavailable,
                    $"URL inválida: {ex.Message}");
            }
        }
        else if (string.Equals(request.Source.Type, "bytes", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Source.Bytes))
                return BuildErrorJson(Guid.Empty, ExtractionErrorCode.SourceUnavailable, "Bytes não fornecidos.");
            try
            {
                source = new ExtractionSource.Bytes(Convert.FromBase64String(request.Source.Bytes));
            }
            catch (FormatException)
            {
                return BuildErrorJson(Guid.Empty, ExtractionErrorCode.UnreadablePdf, "Base64 inválido.");
            }
        }
        else
        {
            return BuildErrorJson(Guid.Empty, ExtractionErrorCode.SourceUnavailable,
                $"Tipo de source '{request.Source.Type}' não suportado.");
        }

        var result = await _extractor.ExtractAsync(new ExtractionInput(
            Source: source,
            ConversationId: conversationId,
            UserId: userId,
            Model: request.Model,
            OutputFormat: request.OutputFormat,
            Features: request.Features,
            CacheEnabled: request.CacheEnabled), ct);

        if (ctx is not null && result.Status is "succeeded" or "cached" && result.CostUsd > 0m)
            ctx.Budget.AddCost(result.CostUsd);

        return BuildJsonResponse(result, request.Model);
    }

    private static string BuildJsonResponse(ExtractionResult result, string model)
    {
        if (result.Status == "failed")
        {
            return JsonSerializer.Serialize(new
            {
                status = "failed",
                jobId = result.JobId.ToString(),
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage,
                detail = result.ErrorDetail,
            }, JsonDefaults.CaseInsensitive);
        }

        return JsonSerializer.Serialize(new
        {
            status = result.Status,
            jobId = result.JobId.ToString(),
            resultRef = result.ResultRef,
            pageCount = result.PageCount,
            cached = result.FromCache,
            costUsd = result.CostUsd,
            model,
            content = result.Content,
            pageAccess = new
            {
                fullKey = result.ResultRef is null ? null : $"{result.ResultRef}:full",
                metaKey = result.ResultRef is null ? null : $"{result.ResultRef}:meta",
            },
        }, JsonDefaults.CaseInsensitive);
    }

    private static string BuildErrorJson(Guid jobId, string errorCode, string errorMessage)
        => JsonSerializer.Serialize(new
        {
            status = "failed",
            jobId = jobId == Guid.Empty ? null : jobId.ToString(),
            errorCode,
            errorMessage,
        }, JsonDefaults.CaseInsensitive);
}
