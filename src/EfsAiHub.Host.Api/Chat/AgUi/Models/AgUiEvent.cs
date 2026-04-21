using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

/// <summary>
/// Evento AG-UI enviado ao frontend via SSE.
/// Cada evento tem um tipo discriminador e payload variável.
/// Campos nulos são omitidos na serialização (WhenWritingNull).
/// </summary>
public sealed record AgUiEvent
{
    public required string Type { get; init; }

    // Lifecycle
    public string? RunId { get; init; }
    public string? ThreadId { get; init; }

    // Steps
    public string? StepId { get; init; }
    public string? StepName { get; init; }

    // Text messages
    public string? MessageId { get; init; }
    public string? Role { get; init; }

    // Output (RUN_FINISHED)
    public string? Output { get; init; }

    // Tool calls
    public string? ToolCallId { get; init; }
    [JsonPropertyName("toolCallName")]
    public string? ToolCallName { get; init; }
    public string? Result { get; init; }
    public string? ParentMessageId { get; init; }

    // State
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Snapshot { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Delta { get; init; }

    // Messages snapshot (resync)
    public AgUiMessage[]? Messages { get; init; }

    // Custom
    public string? CustomName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CustomValue { get; init; }

    // Error
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    // Metadata
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Sequence ID from the PgEventBus envelope. Not serialized to JSON — used only
    /// to emit the SSE "id:" line for reconnect/replay support.
    /// </summary>
    [JsonIgnore]
    public long BusSequenceId { get; init; }
}
