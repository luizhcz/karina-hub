using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Chat.AgUi.Models;
using EfsAiHub.Host.Api.Chat.AgUi.State;
using EfsAiHub.Host.Api.Chat.AgUi.Streaming;

namespace EfsAiHub.Host.Api.Chat.AgUi.Handlers;

/// <summary>
/// Quando o frontend reconecta (perda de conexão, refresh de página),
/// precisa reconstruir o estado sem perder dados.
///
/// Estratégia:
/// 1. Cancela imediatamente o grace period de auto-cancel (AgUiDisconnectRegistry)
/// 2. Frontend envia lastEventId (do SSE retry) → replay dos eventos perdidos
/// 3. Se não tem lastEventId: envia MESSAGES_SNAPSHOT + STATE_SNAPSHOT
/// </summary>
public sealed class AgUiReconnectionHandler
{
    private readonly IWorkflowEventBus _eventBus;
    private readonly AgUiEventMapper _mapper;
    private readonly AgUiSseHandler _sseHandler;
    private readonly AgUiDisconnectRegistry _disconnectRegistry;
    private readonly JsonSerializerOptions _jsonOptions;

    public AgUiReconnectionHandler(
        IWorkflowEventBus eventBus,
        AgUiEventMapper mapper,
        AgUiSseHandler sseHandler,
        AgUiDisconnectRegistry disconnectRegistry)
    {
        _eventBus = eventBus;
        _mapper = mapper;
        _sseHandler = sseHandler;
        _disconnectRegistry = disconnectRegistry;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task HandleReconnectAsync(
        HttpResponse response,
        string executionId,
        string conversationId,
        string? lastEventId,
        AgUiSharedState sharedState,
        IChatMessageRepository messageRepo,
        CancellationToken ct)
    {
        // Cancela o grace period de auto-cancel (usuário voltou antes do timeout)
        _disconnectRegistry.Cancel(executionId);

        // Set SSE headers before any writes — StreamAsync will skip if already set
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        if (lastEventId is not null && long.TryParse(lastEventId, out var seqId))
        {
            // Replay eventos após o último recebido
            await ReplayFromAsync(response, executionId, conversationId, seqId, ct);
        }
        else
        {
            // Full resync
            await _sseHandler.ResyncAsync(response, conversationId,
                sharedState, messageRepo, ct);
        }

        // Continuar streaming normalmente
        await _sseHandler.StreamAsync(response, executionId,
            conversationId, sharedState, ct);
    }

    private async Task ReplayFromAsync(
        HttpResponse response,
        string executionId,
        string conversationId,
        long afterSequenceId,
        CancellationToken ct)
    {
        // Replay do PgEventBus (que persiste em workflow_event_audit)
        var allEvents = await _eventBus.GetHistoryAsync(executionId, ct);
        var missedEvents = allEvents.Where(e => e.SequenceId > afterSequenceId);

        foreach (var envelope in missedEvents)
        {
            var agUiEvents = _mapper.Map(envelope, executionId, conversationId);
            foreach (var evt in agUiEvents)
            {
                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                await response.WriteAsync(
                    $"id: {envelope.SequenceId}\ndata: {json}\n\n", ct);
            }
        }
        await response.Body.FlushAsync(ct);
    }
}
