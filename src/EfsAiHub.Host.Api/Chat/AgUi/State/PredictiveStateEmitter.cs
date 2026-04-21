using System.Text.Json;
using EfsAiHub.Host.Api.Chat.AgUi.Models;
using EfsAiHub.Host.Api.Chat.AgUi.Streaming;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Emite STATE_DELTA parciais durante o streaming de argumentos de tool calls.
/// Para cada chunk de argumentos recebido via AgUiTokenChannel, verifica se o toolName
/// está mapeado em PredictiveStateConfig e emite um STATE_DELTA com o valor parcial.
/// </summary>
public sealed class PredictiveStateEmitter
{
    private readonly AgUiTokenChannel _tokenChannel;
    private readonly AgUiStateManager _stateManager;

    public PredictiveStateEmitter(AgUiTokenChannel tokenChannel, AgUiStateManager stateManager)
    {
        _tokenChannel = tokenChannel;
        _stateManager = stateManager;
    }

    /// <summary>
    /// Emite um STATE_DELTA para o thread quando um TOOL_CALL_ARGS é recebido
    /// e o toolName está mapeado na configuração preditiva.
    /// </summary>
    public async Task EmitIfMappedAsync(
        string threadId,
        string executionId,
        AgUiEvent toolCallArgsEvent,
        PredictiveStateConfig config,
        CancellationToken ct = default)
    {
        if (toolCallArgsEvent.ToolCallName is null || toolCallArgsEvent.Delta is null) return;
        if (!config.ToolNameToStateField.TryGetValue(toolCallArgsEvent.ToolCallName, out var statePath)) return;

        // Delta para TOOL_CALL_ARGS é um JsonElement contendo uma string JSON dos args
        // Tenta extrair e parsear os args parciais para extrair o valor atual
        JsonElement partialValue;
        try
        {
            var argsStr = toolCallArgsEvent.Delta.Value.ValueKind == JsonValueKind.String
                ? toolCallArgsEvent.Delta.Value.GetString() ?? ""
                : toolCallArgsEvent.Delta.Value.GetRawText();
            partialValue = JsonDocument.Parse(argsStr).RootElement.Clone();
        }
        catch
        {
            // Args ainda incompletos (JSON parcial) — ignorar
            return;
        }

        var delta = await _stateManager.SetAgentValueAsync(threadId, statePath, partialValue);
        if (delta is null) return;

        // Escrever o STATE_DELTA no canal para ser enviado ao frontend
        if (_tokenChannel.TryGet(executionId, out var channel))
            await channel!.Writer.WriteAsync(delta, ct);
    }
}
