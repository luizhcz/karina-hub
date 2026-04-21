using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Chat.AgUi.Models;
using EfsAiHub.Host.Api.Chat.AgUi.State;
using EfsAiHub.Host.Api.Chat.AgUi.Streaming;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Chat.AgUi.Handlers;

/// <summary>
/// Streaming AG-UI SSE handler.
/// Consome eventos do PgEventBus + tokens do AgUiTokenChannel,
/// converte para AG-UI events e emite como SSE.
///
/// Disconnect behavior: quando o SSE fecha sem RUN_FINISHED/RUN_ERROR (ex: usuário
/// saiu da página), agenda um grace period antes de cancelar o workflow.
/// Reconexão via GET /reconnect cancela o timer. Execuções com HITL pendente
/// nunca são canceladas por disconnect — o timeout do HITL governa.
/// </summary>
public sealed class AgUiSseHandler
{
    private readonly IWorkflowEventBus _eventBus;
    private readonly AgUiEventMapper _mapper;
    private readonly AgUiTokenChannel _tokenChannel;
    private readonly AgUiDisconnectRegistry _disconnectRegistry;
    private readonly AgUiCancellationHandler _cancellationHandler;
    private readonly IHumanInteractionService _hitlService;
    private readonly IOptions<WorkflowEngineOptions> _engineOptions;
    private readonly ILogger<AgUiSseHandler> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AgUiSseHandler(
        IWorkflowEventBus eventBus,
        AgUiEventMapper mapper,
        AgUiTokenChannel tokenChannel,
        AgUiDisconnectRegistry disconnectRegistry,
        AgUiCancellationHandler cancellationHandler,
        IHumanInteractionService hitlService,
        IOptions<WorkflowEngineOptions> engineOptions,
        ILogger<AgUiSseHandler> logger)
    {
        _eventBus = eventBus;
        _mapper = mapper;
        _tokenChannel = tokenChannel;
        _disconnectRegistry = disconnectRegistry;
        _cancellationHandler = cancellationHandler;
        _hitlService = hitlService;
        _engineOptions = engineOptions;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Inicia streaming AG-UI SSE para uma execução.
    /// Emite STATE_SNAPSHOT inicial, depois converte eventos internos
    /// em eventos AG-UI em tempo real até RUN_FINISHED ou RUN_ERROR.
    /// </summary>
    public async Task StreamAsync(
        HttpResponse response,
        string executionId,
        string conversationId,
        AgUiSharedState sharedState,
        CancellationToken ct,
        string? clientRunId = null)
    {
        // SetSseHeaders may have already been called by the reconnection handler (via ReplayFromAsync
        // or ResyncAsync). Guard to avoid "headers already sent" error.
        if (!response.HasStarted)
            SetSseHeaders(response);

        // Respect client-supplied runId (AG-UI standard) or fall back to executionId
        var runId = clientRunId ?? executionId;
        var threadId = conversationId;

        // Cancela grace period pendente caso o usuário reconectou e abriu novo stream
        _disconnectRegistry.Cancel(executionId);

        // 1. Emitir STATE_SNAPSHOT inicial
        await WriteEventAsync(response, new AgUiEvent
        {
            Type = "STATE_SNAPSHOT",
            Snapshot = sharedState.GetSnapshot()
        }, sequenceId: null, ct);

        // 2. Criar canal de token para esta execução
        var tokenCh = _tokenChannel.GetOrCreate(executionId);

        // 3. Converter eventos do event bus para AG-UI
        var eventBusStream = MapEventBusAsync(executionId, runId, threadId, ct);

        // 4. Merge dos dois streams e emitir
        var completedNormally = false;

        try
        {
            await foreach (var evt in AgUiStreamMerger.MergeAsync(
                eventBusStream, tokenCh.Reader, ct))
            {
                await WriteEventAsync(response, evt, evt.BusSequenceId > 0 ? evt.BusSequenceId : null, ct);

                if (evt.Type is "RUN_FINISHED" or "RUN_ERROR")
                {
                    completedNormally = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — schedule grace period cancel if applicable
            ScheduleGracePeriodIfNeeded(executionId);
        }
        finally
        {
            // 5. Cleanup token channel
            _tokenChannel.Remove(executionId);

            // 6. Explicitly complete the response so Kestrel sends the chunked
            //    transfer encoding terminator (zero-length chunk). Without this,
            //    nginx may close the connection before the terminal chunk is sent,
            //    causing ERR_INCOMPLETE_CHUNKED_ENCODING in the browser.
            try { await response.CompleteAsync(); } catch { /* already closed */ }
        }

        _ = completedNormally; // suppress unused warning
    }

    /// <summary>
    /// Resync após reconexão: envia MESSAGES_SNAPSHOT + STATE_SNAPSHOT.
    /// </summary>
    public async Task ResyncAsync(
        HttpResponse response,
        string conversationId,
        AgUiSharedState sharedState,
        IChatMessageRepository messageRepo,
        CancellationToken ct)
    {
        if (!response.HasStarted)
            SetSseHeaders(response);

        var messages = await messageRepo.GetContextWindowAsync(
            conversationId, maxMessages: 50, ct: ct);

        await WriteEventAsync(response, new AgUiEvent
        {
            Type = "MESSAGES_SNAPSHOT",
            Messages = messages.Select(m => new AgUiMessage(
                m.MessageId, m.Role, m.Content,
                new DateTimeOffset(m.CreatedAt, TimeSpan.Zero))).ToArray()
        }, sequenceId: null, ct);

        await WriteEventAsync(response, new AgUiEvent
        {
            Type = "STATE_SNAPSHOT",
            Snapshot = sharedState.GetSnapshot()
        }, sequenceId: null, ct);
    }

    private void ScheduleGracePeriodIfNeeded(string executionId)
    {
        var gracePeriodSeconds = _engineOptions.Value.DisconnectGracePeriodSeconds;
        if (gracePeriodSeconds <= 0) return;

        // HITL pendente: o workflow já está paused aguardando resposta humana.
        // O timeout do HITL governa — não cancelar por disconnect.
        var hasPendingHitl = _hitlService.GetPendingForExecution(executionId) is not null;
        if (hasPendingHitl)
        {
            _logger.LogDebug(
                "[Disconnect] ExecutionId '{ExecutionId}' tem HITL pendente — grace period não agendado.",
                executionId);
            return;
        }

        _disconnectRegistry.Schedule(
            executionId,
            TimeSpan.FromSeconds(gracePeriodSeconds),
            () => _cancellationHandler.CancelAsync(executionId),
            _logger);

        _logger.LogInformation(
            "[Disconnect] SSE desconectado para '{ExecutionId}'. Grace period de {Seconds}s agendado.",
            executionId, gracePeriodSeconds);
    }

    private async IAsyncEnumerable<AgUiEvent> MapEventBusAsync(
        string executionId,
        string runId,
        string threadId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lastSequenceId = 0L;

        await foreach (var envelope in _eventBus.SubscribeAsync(executionId, ct))
        {
            // Dedup por SequenceId
            if (envelope.SequenceId > 0 && envelope.SequenceId <= lastSequenceId)
                continue;
            if (envelope.SequenceId > 0)
                lastSequenceId = envelope.SequenceId;

            var agUiEvents = _mapper.Map(envelope, runId, threadId);

            foreach (var evt in agUiEvents)
                yield return evt with { BusSequenceId = lastSequenceId };
        }
    }

    private async Task WriteEventAsync(
        HttpResponse response,
        AgUiEvent evt,
        long? sequenceId,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOptions);

        if (sequenceId.HasValue)
            await response.WriteAsync($"id: {sequenceId}\n", ct);

        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static void SetSseHeaders(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no"; // nginx
    }
}
