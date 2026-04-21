using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using EfsAiHub.Host.Api.Chat.AgUi.Models;
using EfsAiHub.Core.Abstractions.AgUi;

namespace EfsAiHub.Host.Api.Chat.AgUi.Streaming;

/// <summary>
/// Canal in-memory por execução para streaming de tokens LLM.
/// Tokens não são persistidos no PgEventBus — vão direto do
/// TokenTrackingChatClient para o AgUiSseHandler via Channel&lt;T&gt;.
/// Implementa IAgUiTokenSink para receber TOOL_CALL_ARGS do TokenTrackingChatClient
/// sem criar dependência circular (Core.Abstractions ← Platform.Runtime → Host.Api).
/// </summary>
public sealed class AgUiTokenChannel : IAgUiTokenSink
{
    private sealed record ChannelEntry(Channel<AgUiEvent> Channel, DateTime CreatedAt);

    private readonly ConcurrentDictionary<string, ChannelEntry> _channels = new();

    /// <summary>Número de channels ativos (para observabilidade).</summary>
    public int Count => _channels.Count;

    public Channel<AgUiEvent> GetOrCreate(string executionId)
    {
        return _channels.GetOrAdd(executionId,
            _ => new ChannelEntry(
                Channel.CreateBounded<AgUiEvent>(new BoundedChannelOptions(1000)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true
                }),
                DateTime.UtcNow)).Channel;
    }

    public bool TryGet(string executionId, out Channel<AgUiEvent>? channel)
    {
        if (_channels.TryGetValue(executionId, out var entry))
        {
            channel = entry.Channel;
            return true;
        }
        channel = null;
        return false;
    }

    public void Remove(string executionId)
    {
        if (_channels.TryRemove(executionId, out var entry))
            entry.Channel.Writer.TryComplete();
    }

    /// <summary>Remove channels órfãos criados há mais de <paramref name="maxAge"/>.</summary>
    /// <returns>Quantidade de channels removidos.</returns>
    internal int RemoveStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;
        foreach (var kvp in _channels)
        {
            if (kvp.Value.CreatedAt < cutoff && _channels.TryRemove(kvp.Key, out var entry))
            {
                entry.Channel.Writer.TryComplete();
                removed++;
            }
        }
        return removed;
    }

    /// <inheritdoc/>
    public void WriteToolCallArgs(string executionId, string toolCallId, string toolName, string argsChunk)
    {
        if (!_channels.TryGetValue(executionId, out var entry)) return;
        entry.Channel.Writer.TryWrite(new AgUiEvent
        {
            Type = "TOOL_CALL_ARGS",
            ToolCallId = toolCallId,
            ToolCallName = toolName,
            Delta = JsonSerializer.SerializeToElement(argsChunk)
        });
    }
}
