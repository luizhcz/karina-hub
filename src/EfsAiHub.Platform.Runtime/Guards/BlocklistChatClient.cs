using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Blocklist;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Middleware fail-secure que aplica a blocklist do projeto ao input (pre-LLM)
/// e ao output (post-LLM) do chat client.
///
/// Pre-LLM: scan ChatRole.User messages. Action=Block lança
/// <see cref="BlocklistViolationException"/> ANTES de chamar o provider — token zero.
/// Action=Redact substitui o conteúdo na message in-place.
///
/// Post-LLM (non-streaming): scan TextContent e FunctionCallContent.Arguments
/// das assistant messages. Block lança após resposta (tokens já consumidos pelo
/// provider). Redact substitui in-place.
///
/// Streaming (v1, simplificado): pre-scan completo + post-scan APENAS no fim
/// do stream (buffer completo). Block publica SAFETY_VIOLATION via IWorkflowEventBus
/// + lança exception. Conteúdo intermediário pode ter sido entregue ao cliente —
/// limitação documentada e marcada pra otimização (sliding-window) em backlog.
///
/// Sem ProjectId em ExecutionContext: throw <see cref="InvalidOperationException"/>
/// (fail-secure — pré-reqs PR 1 e PR 2 cobrem todos os caminhos esperados).
/// </summary>
public sealed class BlocklistChatClient : DelegatingChatClient
{
    private readonly BlocklistEngine _engine;
    private readonly IWorkflowEventBus? _eventBus;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly string _agentId;
    private readonly ILogger _logger;

    public BlocklistChatClient(
        IChatClient inner,
        BlocklistEngine engine,
        IWorkflowEventBus? eventBus,
        IAdminAuditLogger? auditLogger,
        string agentId,
        ILogger logger) : base(inner)
    {
        _engine = engine;
        _eventBus = eventBus;
        _auditLogger = auditLogger;
        _agentId = agentId;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (matcher, ctx) = await ResolveMatcherAsync(cancellationToken);
        if (!matcher.HasPatterns)
            return await base.GetResponseAsync(messages, options, cancellationToken);

        var msgList = messages as IList<ChatMessage> ?? messages.ToList();
        await ScanInputOrApplyAsync(msgList, matcher, ctx, cancellationToken);

        var response = await base.GetResponseAsync(msgList, options, cancellationToken);

        await ScanOutputOrApplyAsync(response, matcher, ctx, cancellationToken);

        return response;
    }

    /// <summary>
    /// Tamanho do tail mantido em hold antes do scan. Cobre 98% dos patterns PII/SECRETS
    /// (CPF=14, CNPJ=18, email~50, JWT prefixo `eyJ` detectável cedo, AWS=20). Patterns
    /// que excedem 128 chars podem ter prefixo emitido antes do block — limitação aceita
    /// e documentada (BUFFER_TAIL_SIZE pode ser ajustado no futuro).
    /// </summary>
    private const int StreamingTailSize = 128;

    /// <summary>
    /// Throttle do scan no streaming output (PR 11 — B2). Em vez de fazer
    /// <c>matcher.FirstMatch(FullBuffer)</c> a cada update do LLM, acumula chars e só faz
    /// scan quando passar deste limiar. Trade-off: cliente pode ver até ScanIntervalChars
    /// chars de potencial conteúdo violador antes do block (cenário pathological raro —
    /// patterns típicos como CPF/JWT casam cedo). Reduz CPU O(K²) acumulado em ~3-4×
    /// pra respostas longas. Scan final sempre roda no fim do stream pra garantir cobertura.
    /// </summary>
    private const int ScanIntervalChars = 512;

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (matcher, ctx) = await ResolveMatcherAsync(cancellationToken);
        if (!matcher.HasPatterns)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
                yield return update;
            yield break;
        }

        var msgList = messages as IList<ChatMessage> ?? messages.ToList();
        await ScanInputOrApplyAsync(msgList, matcher, ctx, cancellationToken);

        // Sliding-window: mantém últimos StreamingTailSize chars em hold pra cobrir
        // patterns que cruzam boundaries entre updates. Texto fora do tail é emitido
        // após scan limpo. Hit no tail interrompe stream antes do conteúdo violador
        // chegar ao cliente. Hit em texto já-emitido (>tail) é tardio mas raro.
        //
        // Throttle (PR 11): scan executa só quando charsSinceLastScan >= ScanIntervalChars.
        // Entre scans, safe text continua sendo emitido (cliente não trava). Trade-off
        // documentado em ScanIntervalChars.
        var window = new SlidingWindowBuffer(StreamingTailSize);
        ChatResponseUpdate? lastUpdate = null;
        var charsSinceLastScan = 0;

        await foreach (var update in base.GetStreamingResponseAsync(msgList, options, cancellationToken))
        {
            lastUpdate = update;

            // Separa text contents (vão pelo sliding window) de outros (passam direto).
            var (collectedText, nonTextContents) = SplitContents(update);

            if (!string.IsNullOrEmpty(collectedText))
            {
                window.Append(collectedText);
                charsSinceLastScan += collectedText.Length;

                // Throttle: só faz scan quando acumulou ScanIntervalChars de novos chars.
                // Hit acumulado entre scans é capturado na próxima iteração que cruzar o limiar
                // ou no scan final pós-stream.
                if (charsSinceLastScan >= ScanIntervalChars)
                {
                    MetricsRegistry.BlocklistScans.Add(1, new KeyValuePair<string, object?>("phase", "output"));
                    var hit = matcher.FirstMatch(window.FullBuffer);
                    charsSinceLastScan = 0;

                    if (hit is not null && hit.Action == BlocklistAction.Block)
                    {
                        // Emite o que já é seguro (até antes do hit OU até o limite do tail), depois throw.
                        var safeBeforeHit = window.SafeTextUpTo(hit.StartIndex);
                        if (safeBeforeHit.Length > 0)
                            yield return BuildTextUpdate(update, safeBeforeHit);

                        var violation = BuildViolation(hit, BlocklistPhase.Output, window.FullBuffer);
                        await EmitMetricAndAuditAsync(violation, ctx, cancellationToken);
                        await PublishSafetyEventAsync(ctx, violation, cancellationToken);
                        throw new BlocklistViolationException(violation);
                    }
                }

                // Sem hit (ou scan ainda não disparado) — emite texto seguro (tudo menos o tail).
                // Cliente recebe progressivamente; scan próximo cobre o que foi emitido se passar
                // pelo tail boundary.
                var safeText = window.DrainSafe();
                if (safeText.Length > 0 || nonTextContents.Count > 0)
                    yield return BuildSyntheticUpdate(update, safeText, nonTextContents);
            }
            else
            {
                // Update sem texto (function call, usage, etc) — passa direto.
                yield return update;
            }
        }

        // Stream terminou. Scan final do tail e emite o que sobrou.
        MetricsRegistry.BlocklistScans.Add(1, new KeyValuePair<string, object?>("phase", "output"));
        var finalHit = matcher.FirstMatch(window.FullBuffer);
        if (finalHit is not null && finalHit.Action == BlocklistAction.Block)
        {
            // Não emite o tail — interrompe stream. Cliente vê SAFETY_VIOLATION + 422.
            var violation = BuildViolation(finalHit, BlocklistPhase.Output, window.FullBuffer);
            await EmitMetricAndAuditAsync(violation, ctx, cancellationToken);
            await PublishSafetyEventAsync(ctx, violation, cancellationToken);
            throw new BlocklistViolationException(violation);
        }

        // Sem hit: emite o tail final.
        var tail = window.Flush();
        if (tail.Length > 0 && lastUpdate is not null)
            yield return BuildTextUpdate(lastUpdate, tail);
    }

    /// <summary>Separa text contents (vão pro sliding window) de não-text (function/usage etc).</summary>
    private static (string CollectedText, List<AIContent> NonTextContents) SplitContents(ChatResponseUpdate update)
    {
        var sb = new StringBuilder();
        var nonText = new List<AIContent>();
        foreach (var c in update.Contents)
        {
            if (c is TextContent tc) sb.Append(tc.Text);
            else nonText.Add(c);
        }
        return (sb.ToString(), nonText);
    }

    /// <summary>Constrói um update sintético com texto safe + non-text originais.</summary>
    private static ChatResponseUpdate BuildSyntheticUpdate(ChatResponseUpdate template, string safeText, List<AIContent> nonText)
    {
        var contents = new List<AIContent>(nonText.Count + 1);
        if (safeText.Length > 0) contents.Add(new TextContent(safeText));
        contents.AddRange(nonText);
        return new ChatResponseUpdate { Contents = contents, Role = template.Role };
    }

    /// <summary>Update apenas com texto (clona role do template pra coerência).</summary>
    private static ChatResponseUpdate BuildTextUpdate(ChatResponseUpdate template, string text)
        => new() { Contents = [new TextContent(text)], Role = template.Role };

    /// <summary>
    /// Buffer com semântica de janela deslizante: append vai sempre no final, drain
    /// retorna texto que está fora do tail (já passou pelo scan e pode ser emitido).
    /// O tail é o que fica em hold pra próxima iteração cobrir patterns boundary.
    /// </summary>
    private sealed class SlidingWindowBuffer
    {
        private readonly StringBuilder _full = new();
        private readonly int _tailSize;
        private int _emittedLength;

        public SlidingWindowBuffer(int tailSize) => _tailSize = tailSize;

        public string FullBuffer => _full.ToString();

        public void Append(string text) => _full.Append(text);

        /// <summary>
        /// Retorna o texto seguro pra emitir (tudo até o hit, se hit cair fora do já-emitido).
        /// Avança o emittedLength pra refletir o que será emitido. Caller deve garantir
        /// que hit.StartIndex aponta pro buffer completo (FullBuffer).
        /// </summary>
        public string SafeTextUpTo(int hitStartIndex)
        {
            // Emite apenas até o início do hit — protege o conteúdo violador.
            var safeEnd = Math.Max(_emittedLength, hitStartIndex);
            if (safeEnd <= _emittedLength) return string.Empty;
            var safe = _full.ToString(_emittedLength, safeEnd - _emittedLength);
            _emittedLength = safeEnd;
            return safe;
        }

        /// <summary>
        /// Emite tudo que está fora do tail. Mantém últimos _tailSize chars em hold.
        /// </summary>
        public string DrainSafe()
        {
            var totalLen = _full.Length;
            var safeEnd = totalLen - _tailSize;
            if (safeEnd <= _emittedLength) return string.Empty;
            var safe = _full.ToString(_emittedLength, safeEnd - _emittedLength);
            _emittedLength = safeEnd;
            return safe;
        }

        /// <summary>Flush final — emite todo o tail restante (no fim do stream).</summary>
        public string Flush()
        {
            var totalLen = _full.Length;
            if (totalLen <= _emittedLength) return string.Empty;
            var rest = _full.ToString(_emittedLength, totalLen - _emittedLength);
            _emittedLength = totalLen;
            return rest;
        }
    }

    private async Task<(BlocklistMatcher Matcher, EfsAiHub.Core.Agents.Execution.ExecutionContext? Ctx)> ResolveMatcherAsync(
        CancellationToken ct)
    {
        var ctx = DelegateExecutor.Current.Value;
        var projectId = ctx?.ProjectId;
        if (string.IsNullOrEmpty(projectId))
        {
            // Fail-secure. Pré-reqs PRs 1 e 2 garantem que ProjectId chega aqui em todos
            // os caminhos esperados. Se isso disparar em prod = caminho não-coberto.
            _logger.LogError(
                "[BlocklistChatClient] ProjectId ausente em ExecutionContext — caminho não esperado. Bloqueando request.");
            throw new InvalidOperationException(
                "BlocklistChatClient requer ProjectId em ExecutionContext (DelegateExecutor.Current.Value).");
        }

        var matcher = await _engine.GetMatcherAsync(projectId, ct);
        return (matcher, ctx);
    }

    private async Task ScanInputOrApplyAsync(
        IList<ChatMessage> messages,
        BlocklistMatcher matcher,
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        CancellationToken ct)
    {
        MetricsRegistry.BlocklistScans.Add(1, new KeyValuePair<string, object?>("phase", "input"));

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.User) continue;

            for (var c = 0; c < msg.Contents.Count; c++)
            {
                if (msg.Contents[c] is not TextContent text) continue;

                var hit = matcher.FirstMatch(text.Text);
                if (hit is null) continue;

                var violation = BuildViolation(hit, BlocklistPhase.Input, text.Text);
                await EmitMetricAndAuditAsync(violation, ctx, ct);

                if (hit.Action == BlocklistAction.Block)
                    throw new BlocklistViolationException(violation);

                if (hit.Action == BlocklistAction.Redact)
                {
                    var (redacted, _) = matcher.Redact(text.Text);
                    msg.Contents[c] = new TextContent(redacted);
                }
                // Warn: já gravou audit + métrica — segue normalmente.
            }
        }
    }

    private async Task ScanOutputOrApplyAsync(
        ChatResponse response,
        BlocklistMatcher matcher,
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        CancellationToken ct)
    {
        MetricsRegistry.BlocklistScans.Add(1, new KeyValuePair<string, object?>("phase", "output"));

        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant) continue;

            for (var c = 0; c < msg.Contents.Count; c++)
            {
                switch (msg.Contents[c])
                {
                    case TextContent text:
                        await ApplyToTextAsync(msg, c, text.Text, matcher, ctx, ct);
                        break;

                    case FunctionCallContent fcc:
                        // Args podem vazar PII se o LLM injetou no payload da tool call.
                        // JsonDefaults.Domain garante UTF-8 não-escapado — patterns com
                        // caracteres não-ASCII (futuros) precisam casar contra texto literal.
                        if (fcc.Arguments is { Count: > 0 })
                        {
                            var argsJson = JsonSerializer.Serialize(fcc.Arguments, JsonDefaults.Domain);
                            var hit = matcher.FirstMatch(argsJson);
                            if (hit is not null && hit.Action == BlocklistAction.Block)
                            {
                                var violation = BuildViolation(hit, BlocklistPhase.Output, argsJson);
                                await EmitMetricAndAuditAsync(violation, ctx, ct);
                                throw new BlocklistViolationException(violation);
                            }
                            // Redact em function args é arriscado (quebra a tool) — só Block aplica.
                        }
                        break;
                }
            }
        }
    }

    private async Task ApplyToTextAsync(
        ChatMessage msg, int contentIndex, string text, BlocklistMatcher matcher,
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        CancellationToken ct)
    {
        var hit = matcher.FirstMatch(text);
        if (hit is null) return;

        var violation = BuildViolation(hit, BlocklistPhase.Output, text);
        await EmitMetricAndAuditAsync(violation, ctx, ct);

        if (hit.Action == BlocklistAction.Block)
            throw new BlocklistViolationException(violation);

        if (hit.Action == BlocklistAction.Redact)
        {
            var (redacted, _) = matcher.Redact(text);
            msg.Contents[contentIndex] = new TextContent(redacted);
        }
    }

    /// <summary>
    /// Emite counter + grava entry no admin_audit_log. Sync-await pra garantir
    /// Dispose do JsonDocument (Task.Run pode falhar a alocar e vazar o buffer).
    /// Audit log é raro (só em violações) e o repositório engole exceções internas,
    /// então o overhead de latência aqui é aceitável dado o trade-off de robustez.
    /// </summary>
    private async Task EmitMetricAndAuditAsync(
        BlocklistViolation violation,
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        CancellationToken ct)
    {
        MetricsRegistry.BlocklistViolations.Add(1,
            new KeyValuePair<string, object?>("phase", violation.Phase.ToString().ToLowerInvariant()),
            new KeyValuePair<string, object?>("category", violation.Category),
            new KeyValuePair<string, object?>("action", violation.Action.ToString()));

        if (_auditLogger is null) return;

        // Audit payload: pattern_id (interno), phase, action, content_hash, context obfuscado.
        // NUNCA inclui o conteúdo cru — ContextObfuscated já substitui o match por [REDACTED-len:N].
        var payload = JsonSerializer.SerializeToDocument(new
        {
            violation_id = violation.ViolationId,
            detected_at = violation.DetectedAt,
            phase = violation.Phase.ToString(),
            pattern_id = violation.PatternId,
            category = violation.Category,
            action_taken = violation.Action.ToString(),
            content_hash = violation.ContentHash,
            context_obfuscated = violation.ContextObfuscated,
            execution_id = ctx?.ExecutionId,
            user_id = ctx?.UserId
        }, JsonDefaults.Domain);

        try
        {
            var entry = new AdminAuditEntry
            {
                ProjectId = ctx?.ProjectId,
                ActorUserId = ctx?.UserId ?? AdminAuditActorTypes.System,
                ActorUserType = AdminAuditActorTypes.Agent,
                Action = AdminAuditActions.BlocklistViolation,
                ResourceType = AdminAuditResources.Blocklist,
                ResourceId = _agentId,
                PayloadAfter = payload,
                Timestamp = violation.DetectedAt.UtcDateTime
            };
            await _auditLogger.RecordAsync(entry, ct);
        }
        finally
        {
            payload.Dispose();
        }
    }

    private static BlocklistViolation BuildViolation(
        BlocklistMatchResult hit, BlocklistPhase phase, string fullText)
    {
        var hash = ComputeContentHash(hit.MatchedText);
        var context = BlocklistMatcher.ObfuscateContext(fullText, hit.StartIndex, hit.Length);
        return new BlocklistViolation(
            ViolationId: Guid.NewGuid(),
            DetectedAt: DateTimeOffset.UtcNow,
            Category: hit.Category,
            PatternId: hit.PatternId,
            Phase: phase,
            Action: hit.Action,
            ContentHash: hash,
            ContextObfuscated: context);
    }

    private static string ComputeContentHash(string content)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(content), hash);
        // Truncado: 16 bytes / 32 hex chars são suficientes pra correlação sem persistir
        // hash full (reduz risco de rainbow table contra conteúdos curtos).
        return Convert.ToHexString(hash[..16]).ToLowerInvariant();
    }

    private async Task PublishSafetyEventAsync(
        EfsAiHub.Core.Agents.Execution.ExecutionContext? ctx,
        BlocklistViolation violation,
        CancellationToken ct)
    {
        if (_eventBus is null)
        {
            // DI esquecida ou não-HTTP path: cliente não receberá SAFETY_VIOLATION via SSE,
            // só vê a exception HTTP 422. Operacional precisa ver isso pra investigar config.
            _logger.LogWarning(
                "[BlocklistChatClient] IWorkflowEventBus indisponível — SAFETY_VIOLATION não emitido. " +
                "Cliente só verá HTTP 422 sem evento SSE terminal. ViolationId={ViolationId}",
                violation.ViolationId);
            return;
        }

        if (ctx?.ExecutionId is not { } executionId)
            return;

        try
        {
            // JsonDefaults.Domain: payload é desserializado pelo AgUiEventMapper que aceita
            // case-insensitive — anonymous types já preservam camelCase, mas o encoder UTF-8
            // garante que textos não-ASCII (mensagens i18n futuras) não vão escapados.
            var payload = JsonSerializer.Serialize(new
            {
                violationId = violation.ViolationId,
                category = violation.Category,
                phase = violation.Phase.ToString(),
                action = violation.Action.ToString(),
                retryable = false,
                message = "Conteúdo violou política do projeto."
            }, JsonDefaults.Domain);

            await _eventBus.PublishAsync(executionId, new WorkflowEventEnvelope
            {
                EventType = "content_violation",
                ExecutionId = executionId,
                Timestamp = violation.DetectedAt.UtcDateTime,
                Payload = payload
            }, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: falha de publish não pode mascarar a exception principal.
            _logger.LogWarning(ex,
                "[BlocklistChatClient] Falha ao publicar SAFETY_VIOLATION via IWorkflowEventBus.");
        }
    }
}
