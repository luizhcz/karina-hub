using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Host.Api.Chat.AgUi;

/// <summary>
/// Converte um evento interno (WorkflowEventEnvelope) em 0-N eventos AG-UI.
/// Um evento interno pode gerar múltiplos AG-UI events
/// (ex: step_completed com texto → STEP_FINISHED + TEXT_MESSAGE_START + CONTENT + END).
/// </summary>
public sealed class AgUiEventMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<AgUiEvent> Map(
        WorkflowEventEnvelope envelope,
        string runId,
        string threadId)
    {
        var payload = TryParsePayload(envelope.Payload);

        return envelope.EventType switch
        {
            // ── Lifecycle ──
            "workflow_started" => [
                new AgUiEvent
                {
                    Type = "RUN_STARTED",
                    RunId = runId,
                    ThreadId = threadId
                }
            ],

            "workflow_completed" => [
                new AgUiEvent
                {
                    Type = "RUN_FINISHED",
                    RunId = runId,
                    ThreadId = threadId,
                    Output = GetString(payload, "output")
                }
            ],

            "workflow_failed" or "workflow_cancelled" or "error" => [
                new AgUiEvent
                {
                    Type = "RUN_ERROR",
                    RunId = runId,
                    Error = GetString(payload, "message") ?? GetString(payload, "error"),
                    ErrorCode = GetString(payload, "category") ?? GetString(payload, "code")
                }
            ],

            // ── Steps ──
            // Bifurcação por nodeType: STEP_* é ciclo de vida de "fala" no chat
            // (spec AG-UI). Executores de código não são falas — saem como CUSTOM
            // executor.lifecycle. Sem nodeType cai no caminho de agente (forward-compat).
            "step_started" or "node_started" => MapNodeStarted(payload),

            "step_completed" or "node_completed" => MapStepCompleted(payload, runId),

            // ── HITL ──
            "hitl_required" => MapHitlRequired(payload),

            "hitl_resolved" => [
                new AgUiEvent
                {
                    Type = "TOOL_CALL_RESULT",
                    ToolCallId = GetString(payload, "interactionId"),
                    Result = GetString(payload, "response") ?? GetString(payload, "result"),
                    MessageId = $"msg-{GetString(payload, "interactionId")}",
                    Role = "tool"
                }
            ],

            // ── Tool call args streaming ──
            "tool_call_args" => [
                new AgUiEvent
                {
                    Type = "TOOL_CALL_ARGS",
                    ToolCallId = GetString(payload, "toolCallId"),
                    ToolCallName = GetString(payload, "toolName"),
                    Delta = JsonSerializer.SerializeToElement(GetString(payload, "argsChunk") ?? "")
                }
            ],

            // ── Token streaming (via TokenBatcher / TokenTrackingChatClient) ──
            "token_delta" or "token" => [
                new AgUiEvent
                {
                    Type = "TEXT_MESSAGE_CONTENT",
                    MessageId = GetString(payload, "messageId"),
                    Delta = JsonSerializer.SerializeToElement(
                        GetString(payload, "content") ?? GetString(payload, "token") ?? "")
                }
            ],

            "text_message_start" => [
                new AgUiEvent
                {
                    Type = "TEXT_MESSAGE_START",
                    MessageId = GetString(payload, "messageId"),
                    Role = GetString(payload, "role") ?? "assistant"
                }
            ],

            "text_message_end" => [
                new AgUiEvent
                {
                    Type = "TEXT_MESSAGE_END",
                    MessageId = GetString(payload, "messageId")
                }
            ],

            // ── Tool invocations ──
            "tool_call_started" => [
                new AgUiEvent
                {
                    Type = "TOOL_CALL_START",
                    ToolCallId = GetString(payload, "invocationId"),
                    ToolCallName = GetString(payload, "toolName"),
                    ParentMessageId = GetString(payload, "nodeId")
                }
            ],

            "tool_call_completed" => [
                new AgUiEvent
                {
                    // AG-UI spec: TOOL_CALL_END has no result field — result belongs to TOOL_CALL_RESULT
                    Type = "TOOL_CALL_END",
                    ToolCallId = GetString(payload, "invocationId")
                }
            ],

            // ── State updates ──
            "state_snapshot" => [
                new AgUiEvent
                {
                    Type = "STATE_SNAPSHOT",
                    Snapshot = payload
                }
            ],

            "state_delta" => [
                new AgUiEvent
                {
                    Type = "STATE_DELTA",
                    Delta = payload
                }
            ],

            // ── Budget exceeded ──
            "budget_exceeded" => [
                new AgUiEvent
                {
                    Type = "RUN_ERROR",
                    RunId = runId,
                    Error = "Budget exceeded",
                    ErrorCode = "BUDGET_EXCEEDED"
                }
            ],

            // ── Blocklist guardrail (PR 6) ──
            // Terminal event — AgUiSseHandler trata SAFETY_VIOLATION como fim do stream.
            // ErrorCode carrega a categoria pública (PII, SECRETS, etc); CustomValue tem
            // o payload completo (violationId, phase, retryable) pra UI renderizar bubble.
            "content_violation" => [
                new AgUiEvent
                {
                    Type = "SAFETY_VIOLATION",
                    RunId = runId,
                    ErrorCode = GetString(payload, "category"),
                    Error = GetString(payload, "message") ?? "Conteúdo violou política do projeto.",
                    CustomValue = payload
                }
            ],

            // ── Escalation ──
            "escalation_requested" => [
                new AgUiEvent
                {
                    Type = "CUSTOM",
                    CustomName = "ESCALATION",
                    CustomValue = payload
                }
            ],

            _ => [] // Eventos desconhecidos são ignorados (forward compatible)
        };
    }

    private IReadOnlyList<AgUiEvent> MapNodeStarted(JsonElement? payload)
    {
        var nodeId = GetString(payload, "nodeId");
        var nodeType = GetString(payload, "nodeType");

        if (IsExecutor(nodeType))
        {
            return [BuildExecutorLifecycle(payload, "started")];
        }

        return [
            new AgUiEvent
            {
                Type = "STEP_STARTED",
                StepId = nodeId,
                StepName = GetString(payload, "agentName") ?? GetString(payload, "name") ?? nodeId
            }
        ];
    }

    private IReadOnlyList<AgUiEvent> MapStepCompleted(JsonElement? payload, string runId)
    {
        var nodeId = GetString(payload, "nodeId");
        var nodeType = GetString(payload, "nodeType");

        if (IsExecutor(nodeType))
        {
            // Output de executor não é "fala" do agente — não reconstroi TEXT_MESSAGE_*.
            return [BuildExecutorLifecycle(payload, "finished")];
        }

        var events = new List<AgUiEvent>
        {
            new()
            {
                Type = "STEP_FINISHED",
                StepId = nodeId,
                StepName = GetString(payload, "agentName") ?? GetString(payload, "name") ?? nodeId
            }
        };

        var output = GetString(payload, "output");
        var wasStreamed = GetBool(payload, "wasStreamed");

        // Agente que produziu output não-streamed → reconstroi os 3 eventos de mensagem
        // pra UI renderizar bubble.
        if (output is not null && !wasStreamed)
        {
            var messageId = $"msg_{nodeId}";
            events.Add(new AgUiEvent
            {
                Type = "TEXT_MESSAGE_START",
                MessageId = messageId,
                Role = "assistant"
            });
            events.Add(new AgUiEvent
            {
                Type = "TEXT_MESSAGE_CONTENT",
                MessageId = messageId,
                Delta = JsonSerializer.SerializeToElement(output)
            });
            events.Add(new AgUiEvent
            {
                Type = "TEXT_MESSAGE_END",
                MessageId = messageId
            });
        }

        return events;
    }

    /// <summary>
    /// Empacota fase do ciclo de vida de executor num CUSTOM event AG-UI.
    /// Shape: { nodeId, phase, functionName?, durationMs? }.
    /// </summary>
    private static AgUiEvent BuildExecutorLifecycle(JsonElement? payload, string phase)
    {
        var meta = new Dictionary<string, object?>
        {
            ["nodeId"] = GetString(payload, "nodeId"),
            ["phase"] = phase,
            ["functionName"] = GetString(payload, "functionName") ?? GetString(payload, "name")
        };
        var duration = GetInt(payload, "durationMs");
        if (duration is not null) meta["durationMs"] = duration;

        return new AgUiEvent
        {
            Type = "CUSTOM",
            CustomName = "executor.lifecycle",
            CustomValue = JsonSerializer.SerializeToElement(meta)
        };
    }

    private static bool IsExecutor(string? nodeType)
        => string.Equals(nodeType, "executor", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<AgUiEvent> MapHitlRequired(JsonElement? payload)
    {
        if (payload is null) return [];

        var interactionId = GetString(payload, "interactionId") ?? Guid.NewGuid().ToString();
        var args = JsonSerializer.Serialize(new
        {
            interactionId,
            question = GetString(payload, "question") ?? "Approval required",
            options = GetStringArray(payload, "options"),
            timeoutSeconds = GetInt(payload, "timeoutSeconds"),
            interactionType = GetString(payload, "interactionType") ?? "Approval"
        });

        return [
            new AgUiEvent { Type = "TOOL_CALL_START", ToolCallId = interactionId, ToolCallName = "request_approval" },
            new AgUiEvent { Type = "TOOL_CALL_ARGS",  ToolCallId = interactionId, Delta = JsonSerializer.SerializeToElement(args) },
            new AgUiEvent { Type = "TOOL_CALL_END",   ToolCallId = interactionId }
        ];
    }

    private static JsonElement? TryParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            return JsonDocument.Parse(payloadJson).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        if (element is null) return null;
        return element.Value.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool GetBool(JsonElement? element, string propertyName)
    {
        if (element is null) return false;
        return element.Value.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               prop.GetBoolean();
    }

    private static int? GetInt(JsonElement? element, string propertyName)
    {
        if (element is null) return null;
        if (!element.Value.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number ? prop.GetInt32() : null;
    }

    private static string[]? GetStringArray(JsonElement? element, string propertyName)
    {
        if (element is null) return null;
        if (!element.Value.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.Array) return null;
        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }
}
