using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Core.Agents.Composition;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Sanitization;
using EfsAiHub.Platform.Runtime.Services;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Middlewares;

/// <summary>
/// Middleware que captura o prompt + payload + resposta enviados ao LLM em
/// cada turno. Comportamento OFF por default — só captura quando admin liga
/// via UI (<see cref="LlmCaptureConfigService"/>). Quando OFF: bypass total,
/// overhead é uma consulta Redis no caminho do request.
///
/// Posição na cadeia: entre <c>RetryingChatClient</c> (fora) e
/// <c>CircuitBreakerChatClient</c> (dentro). Justificativa:
/// <list type="bullet">
///   <item>Vê o request final efetivamente enviado ao provider (Blocklist,
///   OperationalMemory, persona, intents já injetaram seus blocos).</item>
///   <item>Cada tentativa de retry/fallback aparece como row distinta com
///   mesmo <c>TurnId</c> e <c>AttemptIndex</c> diferente — preserva visibilidade
///   sem deduplicar.</item>
/// </list>
///
/// Sanitização: regex padrão (<see cref="ILlmPayloadSanitizer"/>) é aplicada
/// no JSON serializado antes de escrever no channel. Cap 256KB por payload
/// com truncamento direcionado (system/tool preservados; history truncado
/// do meio).
/// </summary>
public sealed class LlmInvocationCaptureChatClient : DelegatingChatClient
{
    private const int MaxPayloadBytes = 256 * 1024;

    private readonly string _agentId;
    private readonly string? _agentVersionId;
    private readonly string _modelId;
    private readonly string _provider;
    private readonly LlmCaptureConfigService _configService;
    private readonly ILlmPayloadSanitizer _sanitizer;
    private readonly ChannelWriter<LlmInvocationLogEntry> _writer;
    private readonly ILogger _logger;

    public LlmInvocationCaptureChatClient(
        IChatClient innerClient,
        string agentId,
        string? agentVersionId,
        string modelId,
        string provider,
        LlmCaptureConfigService configService,
        ILlmPayloadSanitizer sanitizer,
        ChannelWriter<LlmInvocationLogEntry> writer,
        ILogger logger)
        : base(innerClient)
    {
        _agentId = agentId;
        _agentVersionId = agentVersionId;
        _modelId = modelId;
        _provider = provider;
        _configService = configService;
        _sanitizer = sanitizer;
        _writer = writer;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = DelegateExecutor.Current.Value;
        var shouldCapture = await ShouldCaptureAsync(ctx, cancellationToken).ConfigureAwait(false);
        if (!shouldCapture)
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        // Ativa o slot ambient pra contribuidores anotarem suas seções. Caller
        // pode ter ativado antes (workflow runner em modo Graph); preservamos
        // referência existente em vez de sobrescrever.
        var ownsComposition = PromptCompositionAmbient.Current is null;
        if (ownsComposition)
            PromptCompositionAmbient.Current = new PromptComposition();

        // Materializa as messages PRA conseguir capturar (IEnumerable pode ser
        // enumerável-único — passamos a lista direto pro inner).
        var materialized = messages.ToList();
        var sw = Stopwatch.StartNew();
        ChatResponse? response = null;
        Exception? error = null;
        try
        {
            response = await base.GetResponseAsync(materialized, options, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            try
            {
                WriteEntry(ctx, materialized, options, response, error, sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception captureEx)
            {
                _logger.LogWarning(captureEx, "[LlmCapture] Falha ao escrever invocation log — ignorada.");
            }
            if (ownsComposition)
                PromptCompositionAmbient.Current = null;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ctx = DelegateExecutor.Current.Value;
        var shouldCapture = await ShouldCaptureAsync(ctx, cancellationToken).ConfigureAwait(false);
        if (!shouldCapture)
        {
            await foreach (var u in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
                yield return u;
            yield break;
        }

        var ownsComposition = PromptCompositionAmbient.Current is null;
        if (ownsComposition)
            PromptCompositionAmbient.Current = new PromptComposition();

        var materialized = messages.ToList();
        var sw = Stopwatch.StartNew();
        var acc = new StringBuilder();
        UsageDetails? lastUsage = null;
        Exception? error = null;

        // P0-1 fix: try/finally envolvendo o pipeline INTEIRO de streaming pra
        // que erros no acquire de stream OU dentro do await foreach OU
        // exceções do consumer downstream sempre limpem o ambient e gravem o
        // log. Sem isso, AsyncLocal vazaria entre turnos no mesmo
        // ExecutionContext em caso de falha.
        try
        {
            IAsyncEnumerable<ChatResponseUpdate> stream;
            try
            {
                stream = base.GetStreamingResponseAsync(materialized, options, cancellationToken);
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }

            await foreach (var update in stream.ConfigureAwait(false))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent tc) acc.Append(tc.Text);
                    if (content is UsageContent uc) lastUsage = uc.Details;
                }
                yield return update;
            }
        }
        finally
        {
            sw.Stop();
            TryWriteStreaming(ctx, materialized, options, acc, lastUsage, error, sw.Elapsed.TotalMilliseconds);
            if (ownsComposition)
                PromptCompositionAmbient.Current = null;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task<bool> ShouldCaptureAsync(EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx, CancellationToken ct)
    {
        try
        {
            var cfg = await _configService.GetCurrentAsync(ct).ConfigureAwait(false);
            return cfg.Matches(
                projectId: ctx?.ProjectId,
                agentId: _agentId,
                workflowId: ctx?.WorkflowId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LlmCapture] Falha ao ler config — captura desativada nesse turno.");
            return false;
        }
    }

    private void WriteEntry(
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        ChatResponse? response,
        Exception? error,
        double durationMs)
    {
        var responseText = response is null ? string.Empty : ExtractAssistantText(response);
        TryWriteEntry(ctx, messages, options, responseText, response?.Usage, error, durationMs);
    }

    private void TryWriteStreaming(
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        StringBuilder acc,
        UsageDetails? usage,
        Exception? error,
        double durationMs)
    {
        try
        {
            TryWriteEntry(ctx, messages, options, acc.ToString(), usage, error, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LlmCapture] Falha ao escrever invocation log streaming — ignorada.");
        }
    }

    private void TryWriteEntry(
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string responseText,
        UsageDetails? usage,
        Exception? error,
        double durationMs)
    {
        var turnId = ResolveTurnId();
        var attemptIndex = ResolveAttemptIndex();

        var (requestRaw, requestTruncated) = SerializeRequest(messages);
        var (responseRaw, responseTruncated) = SerializeResponse(responseText, usage, error);
        var requestSanitized = _sanitizer.Sanitize(requestRaw);
        var responseSanitized = _sanitizer.Sanitize(responseRaw);
        var truncated = requestTruncated || responseTruncated;

        var chatOptionsSnapshot = options is null ? null : _sanitizer.Sanitize(SerializeChatOptions(options));

        var entry = new LlmInvocationLogEntry(
            Id: null,
            TurnId: turnId,
            AttemptIndex: (short)attemptIndex,
            ExecutionId: ctx?.ExecutionId,
            WorkflowId: ctx?.WorkflowId,
            AgentId: _agentId,
            AgentVersionId: _agentVersionId,
            StepIndex: null,
            ProjectId: ctx?.ProjectId,
            Provider: _provider,
            ProviderResolved: _provider,
            Model: _modelId,
            Intent: ExtractIntent(responseText),
            RequestPayload: requestSanitized,
            ResponsePayload: responseSanitized,
            Composition: PromptCompositionAmbient.Current?.Sections.ToArray(),
            ChatOptionsSnapshot: chatOptionsSnapshot,
            Status: error is null ? "Completed" : "Failed",
            ErrorMessage: error?.Message,
            DurationMs: durationMs,
            InputTokens: (int)(usage?.InputTokenCount ?? 0),
            OutputTokens: (int)(usage?.OutputTokenCount ?? 0),
            CachedTokens: 0,
            RequestSizeBytes: Encoding.UTF8.GetByteCount(requestSanitized),
            ResponseSizeBytes: Encoding.UTF8.GetByteCount(responseSanitized),
            Truncated: truncated,
            CreatedAt: DateTime.UtcNow);

        if (!_writer.TryWrite(entry))
        {
            // Channel saturado — perder uma linha de debug é preferível a bloquear o turno.
            _logger.LogWarning("[LlmCapture] Channel saturado — entry descartada (TurnId={TurnId}).", turnId);
        }
    }

    private static Guid ResolveTurnId()
    {
        var tag = Activity.Current?.GetTagItem("llm.turn.id") as string;
        return Guid.TryParse(tag, out var g) ? g : Guid.NewGuid();
    }

    private static int ResolveAttemptIndex()
    {
        var tag = Activity.Current?.GetTagItem("llm.attempt.index");
        if (tag is int i) return i;
        if (tag is string s && int.TryParse(s, out var parsed)) return parsed;
        return 0;
    }

    private static string ExtractAssistantText(ChatResponse response)
    {
        var first = response.Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .Select(m => string.Concat(m.Contents.OfType<TextContent>().Select(t => t.Text)))
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));
        return first ?? string.Empty;
    }

    private static string? ExtractIntent(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return null;
        try
        {
            var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("intent", out var intent)
                && intent.ValueKind == JsonValueKind.String)
                return intent.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static (string Payload, bool Truncated) SerializeRequest(IReadOnlyList<ChatMessage> messages)
    {
        // Estratégia de truncamento: serializa cada message individualmente,
        // soma sizes, e se passar do cap, trunca history do meio (mantém
        // system, tool, primeiros 3 + últimos 3 user/assistant, e o último
        // user current).
        var msgs = messages.Select(m => new
        {
            role = m.Role.Value,
            content = string.Concat(m.Contents.OfType<TextContent>().Select(t => t.Text)),
            type = ClassifyMessage(m, messages)
        }).ToList();

        var asJson = JsonSerializer.Serialize(msgs);
        if (Encoding.UTF8.GetByteCount(asJson) <= MaxPayloadBytes)
            return (asJson, false);

        // Trunca history (role=user/assistant que não é o último user).
        var historyIndexes = new List<int>();
        for (int i = 0; i < msgs.Count; i++)
        {
            var r = msgs[i].role;
            if (r is "user" or "assistant") historyIndexes.Add(i);
        }
        if (historyIndexes.Count > 7)
        {
            var keepFirst = historyIndexes.Take(3).ToHashSet();
            var keepLast = historyIndexes.TakeLast(3).ToHashSet();
            var truncated = new List<object>(msgs.Count);
            for (int i = 0; i < msgs.Count; i++)
            {
                if (msgs[i].role is "user" or "assistant" && !keepFirst.Contains(i) && !keepLast.Contains(i))
                {
                    if (truncated.Count == 0 || ((dynamic)truncated[^1]).type != "elided")
                    {
                        truncated.Add(new { role = "system", content = "[…elided history messages…]", type = "elided" });
                    }
                    continue;
                }
                truncated.Add(msgs[i]);
            }
            asJson = JsonSerializer.Serialize(truncated);
        }

        // Última defesa: hard truncate string (substring na borda final).
        var bytes = Encoding.UTF8.GetByteCount(asJson);
        if (bytes > MaxPayloadBytes)
        {
            // Pega aproximadamente MaxPayloadBytes chars (UTF-8 1-3 bytes/char).
            var keepChars = MaxPayloadBytes / 2;
            asJson = (asJson.Length > keepChars ? asJson.Substring(0, keepChars) : asJson) + "\"...truncated\"]";
        }
        return (asJson, true);
    }

    private static string ClassifyMessage(ChatMessage msg, IReadOnlyList<ChatMessage> all)
    {
        if (msg.Role == ChatRole.System) return "system";
        if (msg.Role == ChatRole.Tool) return "tool";
        // Última user message recebida = input atual; demais = history.
        if (msg.Role == ChatRole.User && ReferenceEquals(msg, all.LastOrDefault(m => m.Role == ChatRole.User)))
            return "input";
        return msg.Role.Value;
    }

    private static (string Payload, bool Truncated) SerializeResponse(string text, UsageDetails? usage, Exception? error)
    {
        var dto = new
        {
            text,
            inputTokens = (long?)usage?.InputTokenCount,
            outputTokens = (long?)usage?.OutputTokenCount,
            totalTokens = (long?)usage?.TotalTokenCount,
            error = error?.Message,
        };
        var asJson = JsonSerializer.Serialize(dto);
        if (Encoding.UTF8.GetByteCount(asJson) <= MaxPayloadBytes)
            return (asJson, false);

        // Resposta inflada — trunca text e re-serializa.
        var keepChars = MaxPayloadBytes / 3;
        var truncatedText = text.Length > keepChars ? text.Substring(0, keepChars) + "[...truncated]" : text;
        var truncatedDto = new
        {
            text = truncatedText,
            inputTokens = dto.inputTokens,
            outputTokens = dto.outputTokens,
            totalTokens = dto.totalTokens,
            error = dto.error,
        };
        return (JsonSerializer.Serialize(truncatedDto), true);
    }

    private static string SerializeChatOptions(ChatOptions options)
    {
        var dto = new
        {
            instructions = options.Instructions,
            temperature = options.Temperature,
            maxOutputTokens = options.MaxOutputTokens,
            modelId = options.ModelId,
            responseFormat = options.ResponseFormat?.GetType().Name,
            tools = options.Tools?.Select(t => new
            {
                kind = t.GetType().Name,
                name = (t as AIFunction)?.Name,
                description = (t as AIFunction)?.Description,
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto);
    }
}
