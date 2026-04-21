namespace EfsAiHub.Core.Abstractions.AgUi;

/// <summary>
/// Sink para eventos AG-UI de nível de token (chunks de argumentos de tool calls).
/// Implementado pelo AgUiTokenChannel em Host.Api.
/// Definido em Core.Abstractions para evitar dependência circular com Platform.Runtime.
/// </summary>
public interface IAgUiTokenSink
{
    /// <summary>
    /// Escreve um chunk de argumentos de tool call no canal da execução.
    /// Chamado pelo TokenTrackingChatClient durante streaming de respostas LLM.
    /// </summary>
    void WriteToolCallArgs(string executionId, string toolCallId, string toolName, string argsChunk);
}
