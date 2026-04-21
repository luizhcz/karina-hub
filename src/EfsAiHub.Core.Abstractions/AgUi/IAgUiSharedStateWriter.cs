using System.Text.Json;

namespace EfsAiHub.Core.Abstractions.AgUi;

/// <summary>
/// Permite que function tools atualizem o shared state AG-UI da conversa
/// durante a execução do workflow. Cada agente opera em seu namespace
/// (path "agents/{agentId}") para isolamento de draft.
///
/// Definido em Core.Abstractions para evitar dependência circular com Host.Api
/// (mesmo padrão de IAgUiTokenSink).
/// </summary>
public interface IAgUiSharedStateWriter
{
    /// <summary>
    /// Atualiza o shared state no path especificado e emite STATE_DELTA via SSE.
    /// </summary>
    /// <param name="threadId">ID da conversa (= conversationId).</param>
    /// <param name="path">Path no JSON state (ex: "agents/coletor-boleta").</param>
    /// <param name="value">Valor JSON a ser gravado no path.</param>
    Task UpdateAsync(string threadId, string path, JsonElement value);

    /// <summary>
    /// Retorna o snapshot completo do shared state para uma conversa.
    /// Retorna null se não houver state inicializado.
    /// </summary>
    Task<JsonElement?> GetSnapshotAsync(string threadId);
}
